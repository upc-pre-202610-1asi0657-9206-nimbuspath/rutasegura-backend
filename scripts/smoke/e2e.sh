#!/bin/sh
# Prueba de humo de punta a punta: Iam -> Administration -> RabbitMQ -> Trip-Tracking -> RabbitMQ -> Administration.
# Se ejecuta con:  docker compose --profile smoke run --rm smoke
# Requiere: sh, curl, jq y tzdata (la imagen del servicio "smoke" los instala).
set -u

MONO="${MONOLITH_URL:-http://localhost:5000}"
TT="${TRIP_TRACKING_URL:-http://localhost:5180}"
RMQ="${RABBITMQ_API:-http://localhost:15672/api}"
KEY="${PROVISIONING_KEY:?Falta PROVISIONING_KEY}"
RUN=$(date +%s)
PASSWORD="Password123"
PASS=0; FAIL=0
BODY=/tmp/body.json

uuid() { cat /proc/sys/kernel/random/uuid; }
ok()   { PASS=$((PASS+1)); echo "  [OK]   $1"; }
ko()   { FAIL=$((FAIL+1)); echo "  [FAIL] $1"; [ -s "$BODY" ] && echo "         $(head -c 400 "$BODY")"; }
step() { echo; echo "== $1"; }

# call METHOD URL [TOKEN] [JSON] [EXTRA_HEADER]  -> imprime el status; el cuerpo queda en $BODY
call() {
  set -- "$1" "$2" "${3:-}" "${4:-}" "${5:-}"
  if [ -n "$3" ]; then auth="Authorization: Bearer $3"; else auth="X-No-Auth: 1"; fi
  if [ -n "$5" ]; then extra="$5"; else extra="X-Smoke: 1"; fi
  if [ -n "$4" ]; then
    curl -s -o "$BODY" -w '%{http_code}' -X "$1" "$2" -H "$auth" -H "$extra" -H 'Content-Type: application/json' --data "$4"
  else
    curl -s -o "$BODY" -w '%{http_code}' -X "$1" "$2" -H "$auth" -H "$extra"
  fi
}

# expect STATUS DESCRIPCION METHOD URL [TOKEN] [JSON] [EXTRA_HEADER]
expect() {
  want="$1"; desc="$2"; shift 2
  got=$(call "$@")
  if [ "$got" = "$want" ]; then ok "$desc ($got)"; return 0; fi
  ko "$desc: esperado $want, recibido $got"; return 1
}

# wait_until SEGUNDOS DESCRIPCION COMANDO...  (reintenta cada 2 s)
wait_until() {
  secs="$1"; desc="$2"; shift 2
  end=$(( $(date +%s) + secs ))
  while [ "$(date +%s)" -lt "$end" ]; do
    if "$@" >/dev/null 2>&1; then ok "$desc"; return 0; fi
    sleep 2
  done
  ko "$desc (sin respuesta en ${secs}s)"; return 1
}

rmq() { curl -s -u guest:guest "$RMQ/$1"; }
bound() { rmq "exchanges/%2F/$1/bindings/source" | jq -e --arg q "$2" 'any(.[]; .destination == $q)'; }
dlq_empty() { [ "$(rmq "queues/%2F/$1" | jq '.messages // 0')" = "0" ]; }

step "0. Salud y topología de RabbitMQ"
wait_until 90 "Monolito /health" sh -c "curl -fs $MONO/health"
wait_until 90 "Trip-Tracking /health/ready" sh -c "curl -fs $TT/health/ready"
wait_until 60 "trip-tracking.inbound escucha administration.events" bound administration.events trip-tracking.inbound
wait_until 60 "administration.inbound escucha trip-tracking.events" bound trip-tracking.events administration.inbound

step "1. Iam: cuentas y seguridad"
expect 401 "Sin token no se accede a Administration" GET "$MONO/api/v1/routes"
expect 401 "Provisioning sin X-Provisioning-Key" POST "$MONO/api/v1/organizations" "" '{"name":"x","administrator":{"firstName":"a","lastName":"b","email":"x@x.pe","password":"Password123"}}'
ADMIN_EMAIL="admin.$RUN@smoke.pe"
expect 201 "Provisioning: organización + su único ADMIN" POST "$MONO/api/v1/organizations" "" \
  "{\"name\":\"Movilidad Smoke $RUN\",\"administrator\":{\"firstName\":\"Ana\",\"lastName\":\"Smoke\",\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PASSWORD\"}}" \
  "X-Provisioning-Key: $KEY" || exit 1
ORG_ID=$(jq -r '.organization.id' "$BODY")
expect 403 "Sign-up como ADMIN está prohibido" POST "$MONO/api/v1/users" "" \
  "{\"firstName\":\"Eve\",\"lastName\":\"X\",\"email\":\"eve.$RUN@smoke.pe\",\"password\":\"$PASSWORD\",\"roleTier\":\"ADMIN\"}"
expect 400 "Contraseña débil rechazada" POST "$MONO/api/v1/users" "" \
  "{\"firstName\":\"Pepe\",\"lastName\":\"X\",\"email\":\"weak.$RUN@smoke.pe\",\"password\":\"abc\"}"
PARENT_EMAIL="padre.$RUN@smoke.pe"
expect 201 "Sign-up de padre desde la app" POST "$MONO/api/v1/users" "" \
  "{\"firstName\":\"Luis\",\"lastName\":\"Padre\",\"email\":\"$PARENT_EMAIL\",\"password\":\"$PASSWORD\"}"
expect 401 "Sign-in con contraseña incorrecta" POST "$MONO/api/v1/users/sign-in" "" "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"Incorrecta999\"}"
expect 200 "Sign-in del ADMIN" POST "$MONO/api/v1/users/sign-in" "" "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$PASSWORD\"}" || exit 1
ADMIN=$(jq -r '.token' "$BODY")
expect 200 "Sign-in del padre" POST "$MONO/api/v1/users/sign-in" "" "{\"email\":\"$PARENT_EMAIL\",\"password\":\"$PASSWORD\"}"
PARENT=$(jq -r '.token' "$BODY")
expect 403 "Un padre no administra rutas" GET "$MONO/api/v1/routes" "$PARENT"
DRIVER_EMAIL="conductor.$RUN@smoke.pe"
expect 201 "El ADMIN crea la cuenta del conductor" POST "$MONO/api/v1/users/drivers" "$ADMIN" \
  "{\"firstName\":\"Carlos\",\"lastName\":\"Ramirez\",\"email\":\"$DRIVER_EMAIL\",\"password\":\"$PASSWORD\"}" || exit 1
DRIVER_USER_ID=$(jq -r '.id' "$BODY")
expect 200 "Sign-in del conductor" POST "$MONO/api/v1/users/sign-in" "" "{\"email\":\"$DRIVER_EMAIL\",\"password\":\"$PASSWORD\"}" || exit 1
DRIVER=$(jq -r '.token' "$BODY")
expect 200 "El ADMIN lista las cuentas de su organización" GET "$MONO/api/v1/users" "$ADMIN"
expect 200 "GET /users/me" GET "$MONO/api/v1/users/me" "$DRIVER"

step "2. Administration: Roster y Route Planning (comandos)"
DOC=$(printf '%08d' $((RUN % 100000000)))
expect 201 "Registrar conductor en el Roster" POST "$MONO/api/v1/roster/drivers" "$ADMIN" \
  "{\"userId\":\"$DRIVER_USER_ID\",\"firstName\":\"Carlos\",\"lastName\":\"Ramirez\",\"documentNumber\":\"$DOC\",\"licenseNumber\":\"Q$DOC\",\"licenseCategory\":\"A-IIb\",\"licenseExpiresOn\":\"2030-12-31\",\"phoneNumber\":\"987654321\"}" || exit 1
DRIVER_ID=$(jq -r '.id' "$BODY")
expect 200 "Verificar conductor" POST "$MONO/api/v1/roster/drivers/$DRIVER_ID/verify" "$ADMIN" '{}'
expect 200 "El conductor ve su perfil" GET "$MONO/api/v1/roster/drivers/me" "$DRIVER"
PLATE="ABC-$(printf '%03d' $((RUN % 1000)))"
expect 201 "Registrar vehículo" POST "$MONO/api/v1/roster/vehicles" "$ADMIN" \
  "{\"licensePlate\":\"$PLATE\",\"seatingCapacity\":15,\"brand\":\"Toyota\",\"model\":\"Hiace\"}" || exit 1
VEHICLE_ID=$(jq -r '.id' "$BODY")

# Salida en 15 min (hora de Lima) y llegada hasta 60 min después, sin cruzar la medianoche:
# la API exige que la llegada sea posterior a la salida dentro del mismo día.
lima_minute() { h=$(TZ=America/Lima date -d "@$1" +%H); m=$(TZ=America/Lima date -d "@$1" +%M); echo $(( ${h#0} * 60 + ${m#0} )); }
DEP_EPOCH=$(( $(date +%s) + 900 ))
[ "$(lima_minute $DEP_EPOCH)" -ge 1439 ] && DEP_EPOCH=$(( DEP_EPOCH + 120 ))   # 23:59 -> 00:01
REMAINING=$(( 1439 - $(lima_minute $DEP_EPOCH) ))
DURATION=$(( REMAINING < 60 ? REMAINING : 60 ))
DEPARTURE=$(TZ=America/Lima date -d "@$DEP_EPOCH" +%H:%M:00)
ARRIVAL=$(TZ=America/Lima date -d "@$(( DEP_EPOCH + DURATION * 60 ))" +%H:%M:00)
ROUTE_NAME="Ruta Smoke $RUN"
echo "  Salida $DEPARTURE / llegada $ARRIVAL (hora de Lima)"
expect 201 "Crear ruta" POST "$MONO/api/v1/routes" "$ADMIN" \
  "{\"name\":\"$ROUTE_NAME\",\"direction\":\"PICKUP\",\"departureTime\":\"$DEPARTURE\",\"estimatedArrivalTime\":\"$ARRIVAL\",\"timeZoneId\":\"America/Lima\",\"operatingDays\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\",\"Saturday\",\"Sunday\"]}" || exit 1
ROUTE_ID=$(jq -r '.id' "$BODY")
expect 200 "Agregar parada 1" POST "$MONO/api/v1/routes/$ROUTE_ID/stops" "$ADMIN" \
  '{"name":"Av. Primavera 456","address":"Av. Primavera 456, Surco","latitude":-12.11234,"longitude":-76.98567}'
expect 200 "Agregar parada 2" POST "$MONO/api/v1/routes/$ROUTE_ID/stops" "$ADMIN" \
  '{"name":"Calle Los Olivos 123","latitude":-12.10890,"longitude":-76.99120}'
STOP_ID=$(jq -r '.stops[0].id' "$BODY")
STUDENT_ID=$(uuid)
expect 200 "Asignar alumno a la parada 1" PUT "$MONO/api/v1/routes/$ROUTE_ID/stops/$STOP_ID/students/$STUDENT_ID" "$ADMIN" '{}'
expect 200 "Asignar conductor y vehículo (RouteAssigned)" PUT "$MONO/api/v1/routes/$ROUTE_ID/assignment" "$ADMIN" \
  "{\"driverId\":\"$DRIVER_ID\",\"vehicleId\":\"$VEHICLE_ID\"}"
expect 200 "Activar ruta (RoutePublished)" POST "$MONO/api/v1/routes/$ROUTE_ID/activate" "$ADMIN" '{}'
expect 200 "Cupo de rutas activas del plan" GET "$MONO/api/v1/routes/quota" "$ADMIN" && echo "         $(cat "$BODY")"

step "3. Evento Administration -> Trip-Tracking (route-published)"
trip_scheduled() {
  call GET "$TT/api/v1/drivers/me/trips" "$DRIVER" >/dev/null
  jq -e --arg n "$ROUTE_NAME" 'any(.[]; .routeName == $n)' "$BODY"
}
wait_until 90 "Trip-Tracking recibió la ruta y programó el viaje del conductor" trip_scheduled || { echo "  Revisa: docker compose logs trip-tracking monolith"; exit 1; }
TRIP_ID=$(jq -r --arg n "$ROUTE_NAME" '[.[] | select(.routeName == $n)][0].tripId' "$BODY")
echo "  Viaje $TRIP_ID"

step "4. Trip-Tracking: viaje del conductor (comandos con token de Iam)"
expect 200 "Iniciar viaje" POST "$TT/api/v1/trips/$TRIP_ID/start" "$DRIVER" "{\"clientEventId\":\"$(uuid)\"}"
expect 200 "Reportar incidente (IncidentReported)" POST "$TT/api/v1/trips/$TRIP_ID/incidents" "$DRIVER" \
  "{\"clientEventId\":\"$(uuid)\",\"incidentType\":\"HEAVY_TRAFFIC\",\"detail\":\"Prueba de humo\",\"notifyGuardians\":false}"
expect 200 "SOS (SosRaised)" POST "$TT/api/v1/trips/$TRIP_ID/sos" "$DRIVER" "{\"clientEventId\":\"$(uuid)\"}"
expect 204 "El ADMIN atiende el SOS (SosResolved)" POST "$TT/api/v1/trips/$TRIP_ID/sos/resolve" "$ADMIN" '{}'
expect 200 "Marcar al alumno como ausente" POST "$TT/api/v1/trips/$TRIP_ID/absences" "$DRIVER" \
  "{\"studentId\":\"$STUDENT_ID\",\"clientEventId\":\"$(uuid)\"}"
expect 200 "Finalizar viaje (TripCompleted)" POST "$TT/api/v1/trips/$TRIP_ID/complete" "$DRIVER" "{\"clientEventId\":\"$(uuid)\"}"

step "5. Eventos Trip-Tracking -> Administration"
alert_is() {
  call GET "$MONO/api/v1/operations/alerts?onlyOpen=false" "$ADMIN" >/dev/null
  jq -e --arg t "$1" --arg s "$2" --arg trip "$TRIP_ID" 'any(.[]; .type == $t and .status == $s and .tripId == $trip)' "$BODY"
}
wait_until 60 "Alerta INCIDENT abierta en Operations" alert_is INCIDENT OPEN
wait_until 60 "Alerta SOS resuelta en Operations" alert_is SOS RESOLVED
record_exists() {
  call GET "$MONO/api/v1/routes/$ROUTE_ID/trip-records" "$ADMIN" >/dev/null
  jq -e --arg trip "$TRIP_ID" 'any(.[]; .tripId == $trip)' "$BODY"
}
wait_until 60 "Registro del viaje completado en la ruta" record_exists && echo "         $(jq -c '.[0] | {absentStudents, incidentCount, durationMinutes}' "$BODY")"

step "6. Retiro de la ruta (route-retired)"
expect 200 "Desactivar ruta" POST "$MONO/api/v1/routes/$ROUTE_ID/deactivate" "$ADMIN" '{}'

step "7. Mensajes fallidos (DLQ)"
wait_until 10 "administration.inbound.dlq vacía" dlq_empty administration.inbound.dlq
wait_until 10 "trip-tracking.inbound.dlq vacía" dlq_empty trip-tracking.inbound.dlq

echo
echo "Resultado: $PASS OK, $FAIL FAIL"
[ "$FAIL" -eq 0 ]
