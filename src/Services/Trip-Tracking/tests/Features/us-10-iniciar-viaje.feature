# language: es

Característica: US-10 Iniciar el viaje asignado
  Como conductor
  Quiero iniciar un viaje que tengo asignado
  Para comenzar el recorrido y habilitar su seguimiento

  Escenario: Iniciar correctamente un viaje asignado
    Dado un viaje programado asignado al conductor
    Y el inicio se encuentra dentro de la ventana permitida
    Cuando el conductor inicia el viaje
    Entonces la operación debe aplicarse correctamente
    Y el estado del viaje debe cambiar a InProgress
    Y debe registrarse la hora de inicio
    Y debe generarse un evento TripStarted

  Escenario: Rechazar el inicio realizado por otro conductor
    Dado un viaje programado asignado a un conductor
    Cuando otro conductor intenta iniciar el viaje
    Entonces la acción debe rechazarse con DriverMismatch
    Y el estado del viaje debe permanecer Scheduled

  Escenario: Rechazar un inicio realizado demasiado temprano
    Dado un viaje programado asignado al conductor
    Cuando el conductor intenta iniciar el viaje más de una hora antes
    Entonces la acción debe rechazarse con StartTooEarly

  Escenario: Iniciar un viaje con retraso
    Dado un viaje programado asignado al conductor
    Cuando el conductor inicia el viaje más de diez minutos después de la hora programada
    Entonces el estado del viaje debe cambiar a Delayed
    Y debe generarse un evento TripDelayed

  Escenario: Reprocesar el mismo inicio de manera idempotente
    Dado un viaje programado asignado al conductor
    Y el viaje ya fue iniciado con un clientEventId
    Cuando se procesa nuevamente el mismo clientEventId
    Entonces la segunda operación no debe aplicarse
    Y debe existir un solo evento TripStarted

  Escenario: Registrar una lectura GPS válida durante un viaje activo
    Dado un viaje activo asignado al conductor
    Y existe una lectura GPS válida
    Cuando el servidor procesa la posición recibida
    Entonces la lectura debe ser aceptada
    Y debe actualizarse la última posición del viaje
    Y debe conservarse la fecha de recepción
    Y debe conservarse la velocidad registrada

  Escenario: Ignorar una lectura GPS duplicada o antigua
    Dado un viaje activo con una posición previamente aceptada
    Cuando el servidor recibe una lectura con la misma fecha o una fecha anterior
    Entonces la lectura debe ignorarse como StaleOrDuplicate
    Y la última posición aceptada no debe ser reemplazada

  Escenario: Ignorar una lectura GPS con baja precisión
    Dado un viaje activo
    Cuando el servidor recibe una lectura GPS con baja precisión
    Entonces la lectura debe ignorarse como LowAccuracy
    Y no debe actualizarse la última posición del viaje

  Escenario: Rechazar seguimiento cuando el viaje no está activo
    Dado un viaje que todavía se encuentra Scheduled
    Cuando el conductor intenta registrar una posición GPS
    Entonces la acción debe rechazarse con RequiresActiveTrip