# language: es

Característica: US-11 Registrar abordaje y desembarque
  Como conductor
  Quiero registrar el abordaje y desembarque de los alumnos del viaje
  Para mantener la trazabilidad de su traslado durante el recorrido

  Escenario: Registrar el abordaje de un alumno asignado a un viaje activo
    Dado un viaje activo con un alumno asignado
    Y el alumno se encuentra pendiente de abordar
    Cuando el conductor registra el abordaje del alumno
    Entonces el estado del alumno debe cambiar a Boarded
    Y debe conservarse la fecha del abordaje
    Y debe conservarse la ubicación cuando esté disponible
    Y debe generarse un evento de dominio StudentBoarded

  Escenario: Rechazar el abordaje de un alumno que no pertenece al viaje
    Dado un viaje activo
    Y un alumno que no está asignado al viaje
    Cuando el conductor intenta registrar su abordaje
    Entonces la acción debe rechazarse con StudentNotInRoster

  Escenario: Reprocesar el mismo abordaje de forma idempotente
    Dado un viaje activo con un alumno asignado
    Y el abordaje ya fue registrado con un clientEventId
    Cuando se procesa nuevamente el mismo clientEventId
    Entonces no debe generarse otro evento StudentBoarded

  Escenario: Registrar el desembarque de un alumno que se encuentra a bordo
    Dado un viaje activo de retorno
    Y el alumno se encuentra en estado Boarded
    Cuando el conductor registra el desembarque
    Entonces el estado del alumno debe cambiar a Alighted
    Y debe generarse un evento de dominio StudentAlighted