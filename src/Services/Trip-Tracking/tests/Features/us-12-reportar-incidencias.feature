# language: es

Característica: US-12 Reportar incidencias del viaje
  Como conductor
  Quiero reportar incidencias durante un viaje activo
  Para mantener informada a la operación sobre situaciones ocurridas durante el recorrido

  Escenario: Registrar tránsito intenso y retrasar el viaje
    Dado un viaje activo
    Cuando el conductor reporta una incidencia HeavyTraffic
    Y solicita notificar a los tutores
    Entonces la incidencia debe quedar registrada
    Y el estado del viaje debe cambiar a Delayed
    Y debe generarse un evento IncidentReported
    Y debe generarse un evento TripDelayed

  Escenario: Registrar malestar de un estudiante sin retrasar el viaje
    Dado un viaje activo
    Cuando el conductor reporta una incidencia StudentUnwell
    Entonces la incidencia debe quedar registrada
    Y el estado del viaje debe permanecer InProgress
    Y no debe generarse un evento TripDelayed

  Escenario: Rechazar una incidencia Other sin detalle
    Dado un viaje activo
    Cuando el conductor reporta una incidencia Other sin un detalle válido
    Entonces la acción debe rechazarse con IncidentDetailRequired

  Escenario: Rechazar una incidencia cuyo detalle supera el límite permitido
    Dado un viaje activo
    Cuando el conductor reporta una incidencia con más de 500 caracteres de detalle
    Entonces la acción debe rechazarse con IncidentDetailTooLong

  Escenario: Reprocesar una incidencia de forma idempotente
    Dado un viaje activo
    Y una incidencia fue registrada con un clientEventId
    Cuando se procesa nuevamente el mismo clientEventId
    Entonces la acción no debe aplicarse nuevamente
    Y debe existir una sola incidencia registrada