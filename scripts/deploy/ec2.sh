#!/bin/sh
set +x
set -eu
umask 077
cd "${EC2_DEPLOY_DIR:-/opt/rutasegura}"

case "${EC2_SERVICE:-}" in
  gateway|monolith|trip-tracking|notification) set -- "$EC2_SERVICE" ;;
  configuration) set -- gateway monolith trip-tracking notification ;;
  *) echo "Unknown EC2 deployment target." >&2; exit 1 ;;
esac

# Serialize configuration changes and image deployments on this host.
exec 9>.deploy.lock
flock -w 120 9

for sequence in "${EC2_RELEASE_NUMBER:-}" "${EC2_RELEASE_ATTEMPT:-}"; do
  case "$sequence" in
    ''|*[!0-9]*) echo "Invalid workflow sequence." >&2; exit 1 ;;
  esac
done

state=.deploy-state
mkdir -p "$state"
release="$state/$EC2_SERVICE.release"
if [ -f "$release" ]; then
  read -r applied_number applied_attempt _ < "$release"
  if [ "$EC2_RELEASE_NUMBER" -lt "$applied_number" ] ||
      { [ "$EC2_RELEASE_NUMBER" -eq "$applied_number" ] && [ "$EC2_RELEASE_ATTEMPT" -le "$applied_attempt" ]; }; then
    echo "Skipping this execution because a newer or identical release is already active."
    exit 0
  fi
fi

staging=$(mktemp -d .deploy.XXXXXX)
ors_tmp=
fcm_tmp=
fcm_active=false
cleanup() {
  rm -f -- "$staging/compose.yml" "$staging/fcm.yml" "$staging/images.yml" "$staging/release"
  if [ -n "$ors_tmp" ]; then rm -f -- "$ors_tmp"; fi
  if [ -n "$fcm_tmp" ]; then rm -f -- "$fcm_tmp"; fi
  rmdir -- "$staging"
}
trap cleanup EXIT
if [ "$EC2_SERVICE" = configuration ] || [ ! -f "$state/compose.yml" ] || [ ! -f "$state/fcm.yml" ]; then
  config_override="$staging/compose.yml"
  fcm_template="$staging/fcm.yml"
  printf '%s' "$EC2_COMPOSE_OVERRIDE" | base64 -d > "$config_override"
  printf '%s' "$EC2_FCM_OVERRIDE" | base64 -d > "$fcm_template"
else
  config_override="$state/compose.yml"
  fcm_template="$state/fcm.yml"
fi

# Use published image references for previously deployed services, including on a config-only run.
for service in gateway monolith trip-tracking notification; do
  image=
  if [ "$service" = "$EC2_SERVICE" ]; then
    image=${EC2_IMAGE:?Missing release image}
  elif [ -f "$state/$service.release" ]; then
    read -r _ _ image < "$state/$service.release"
  fi
  if [ -n "$image" ]; then
    if ! printf '%s\n' "$image" | grep -Eq '^[a-zA-Z0-9][a-zA-Z0-9._/:@-]+$'; then
      echo "Invalid image reference for $service." >&2
      exit 1
    fi
    if [ ! -s "$staging/images.yml" ]; then printf 'services:\n' > "$staging/images.yml"; fi
    printf '  %s:\n    image: "%s"\n' "$service" "$image" >> "$staging/images.yml"
  fi
done
if [ ! -s "$staging/images.yml" ]; then printf 'services: {}\n' > "$staging/images.yml"; fi

if [ "$EC2_SERVICE" = monolith ] && [ -f ors-secret-id ]; then
  ors_secret_id=$(cat ors-secret-id)
  if [ -z "$ors_secret_id" ]; then
    echo "ORS: EC2 has an empty secret identifier." >&2
    exit 1
  fi

  mkdir -p secrets/ors
  chmod 0700 secrets/ors
  ors_tmp=$(mktemp secrets/ors/.key.XXXXXX)
  aws secretsmanager get-secret-value --region "$EC2_AWS_REGION" \
    --secret-id "$ors_secret_id" --query SecretString --output text --no-cli-pager > "$ors_tmp"

  if ! awk 'NR == 1 && $0 ~ /^[^[:space:]]+$/ && $0 != "None" && substr($0, 1, 1) != "{" && substr($0, 1, 1) != "[" { valid = 1 } END { exit !(NR == 1 && valid) }' "$ors_tmp"; then
    echo "ORS: expected a nonempty plaintext API key." >&2
    exit 1
  fi
fi

if [ -n "${EC2_FCM_SECRET_ID:-}" ]; then
  case "$EC2_SERVICE" in
    configuration|notification)
      mkdir -p secrets/notification-fcm
      chmod 0700 secrets/notification-fcm
      fcm_tmp=$(mktemp secrets/notification-fcm/.firebase.XXXXXX)
      aws secretsmanager get-secret-value --region "$EC2_AWS_REGION" \
        --secret-id "$EC2_FCM_SECRET_ID" --query SecretString --output text --no-cli-pager > "$fcm_tmp"

      if ! fcm_project=$(python3 -c '
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
if data.get("type") != "service_account" or not all(data.get(key) for key in ("project_id", "client_email", "private_key")):
    sys.exit(1)
print(data["project_id"])' "$fcm_tmp" 2>/dev/null); then
        echo "FCM: expected a Firebase service account credential." >&2
        exit 1
      fi
      if [ -n "${NOTIFICATION_FCM_PROJECT_ID:-}" ] && [ "$NOTIFICATION_FCM_PROJECT_ID" != "$fcm_project" ]; then
        echo "FCM: the credential belongs to a different Firebase project." >&2
        exit 1
      fi

      export NOTIFICATION_FCM_CREDENTIALS_FILE="$PWD/secrets/notification-fcm/firebase-admin.json"
      export NOTIFICATION_FCM_PROJECT_ID="$fcm_project"
      fcm_active=true
      ;;
  esac
fi

compose() {
  if [ "$fcm_active" = true ]; then
    docker compose -f docker-compose.prod.yml -f "$config_override" -f "$staging/images.yml" -f "$fcm_template" "$@"
  else
    docker compose -f docker-compose.prod.yml -f "$config_override" -f "$staging/images.yml" "$@"
  fi
}
compose config --quiet

if [ "$EC2_SERVICE" != configuration ]; then
  ecr_password=$(aws ecr get-login-password --region "$EC2_AWS_REGION")
  printf '%s' "$ecr_password" | docker login --username AWS --password-stdin "$EC2_ECR_REGISTRY"
  unset ecr_password
  compose pull "$@"
fi

# The enclosing host directories are private. Non-root containers read the mounted files.
if [ -n "$ors_tmp" ]; then
  chmod 0444 "$ors_tmp"
  mv -f -- "$ors_tmp" secrets/ors/Administration__Maps__ApiKey
  ors_tmp=
  # ORS is read on startup. Recreate the container even if the image is unchanged.
  set -- --force-recreate "$@"
fi
if [ -n "$fcm_tmp" ]; then
  chmod 0444 "$fcm_tmp"
  if ! cmp -s "$fcm_tmp" "$NOTIFICATION_FCM_CREDENTIALS_FILE"; then
    mv -f -- "$fcm_tmp" "$NOTIFICATION_FCM_CREDENTIALS_FILE"
    fcm_tmp=
    # Recreate only Notification if a bind-mounted credential changed in a config-only deploy.
    if [ "$EC2_SERVICE" = configuration ]; then
      compose up -d --no-deps --pull never --force-recreate --wait notification
      set -- gateway monolith trip-tracking
    else
      set -- --force-recreate "$@"
    fi
  fi
fi
compose up -d --no-deps --pull never --wait "$@"

if [ "$EC2_SERVICE" = configuration ]; then
  mv -f -- "$config_override" "$state/compose.yml"
  mv -f -- "$fcm_template" "$state/fcm.yml"
fi
printf '%s %s %s\n' "$EC2_RELEASE_NUMBER" "$EC2_RELEASE_ATTEMPT" "${EC2_IMAGE:-}" > "$staging/release"
mv -f -- "$staging/release" "$release"
