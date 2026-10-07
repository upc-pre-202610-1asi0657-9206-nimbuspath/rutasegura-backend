# language: es

Característica: US-14 Finalizar el viaje y generar su resumen
  Como conductor
  Quiero finalizar el viaje que estoy realizando
  Para cerrar el recorrido y conservar su resultado final

  Escenario: Finalizar un viaje de ida con todos los estudiantes registrados
    Dado un viaje activo de ida
    Y todos los pasajeros fueron registrados como abordados o ausentes
    Y no existe una alerta SOS activa
    Cuando el conductor finaliza el viaje
    Entonces el estado del viaje debe cambiar a Completed
    Y los estudiantes abordados deben quedar en estado Alighted
    Y debe generarse un evento TripCompleted
    Y el evento debe contener los estudiantes entregados
    Y debe contener los estudiantes ausentes
    Y debe contener la cantidad de incidencias registradas

  Escenario: Rechazar finalización con estudiantes pendientes
    Dado un viaje activo
    Y existe al menos un pasajero en estado Pending
    Cuando el conductor intenta finalizar el viaje
    Entonces la acción debe rechazarse con HasPendingStudents
    Y el estado del viaje debe permanecer InProgress

  Escenario: Rechazar finalización de retorno con estudiantes a bordo
    Dado un viaje activo de retorno
    Y existe al menos un estudiante en estado Boarded
    Cuando el conductor intenta finalizar el viaje
    Entonces la acción debe rechazarse con HasStudentsOnBoard

  Escenario: Finalizar un viaje de retorno cuando todos descendieron
    Dado un viaje activo de retorno
    Y todos los estudiantes abordaron y posteriormente desembarcaron
    Cuando el conductor finaliza el viaje
    Entonces el estado del viaje debe cambiar a Completed

  Escenario: Rechazar finalización mientras existe una alerta SOS activa
    Dado un viaje activo
    Y existe una alerta SOS activa
    Cuando el conductor intenta finalizar el viaje
    Entonces la acción debe rechazarse con HasActiveSos

  Escenario: Rechazar la finalización de un viaje que no ha iniciado
    Dado un viaje programado
    Cuando el conductor intenta finalizar el viaje
    Entonces la acción debe rechazarse con InvalidTransition