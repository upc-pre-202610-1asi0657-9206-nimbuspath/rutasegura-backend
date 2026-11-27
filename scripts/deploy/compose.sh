#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
[[ -f .env.production ]] || { echo 'Provision .env.production first.' >&2; exit 1; }
files=(-f docker-compose.yml -f docker-compose.prod.yml)
fcm=$(sed -n 's/^NOTIFICATION_FCM_ENABLED=//p' .env.production | tr -d '\r')
case "$fcm" in
  true) files+=(-f docker-compose.notification.fcm.yml) ;;
  false|'') ;;
  *) echo 'NOTIFICATION_FCM_ENABLED must be true or false.' >&2; exit 1 ;;
esac
exec docker compose --env-file .env.production "${files[@]}" "$@"
