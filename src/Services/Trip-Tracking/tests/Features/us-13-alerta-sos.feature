# language: es

Característica: US-13 Activar y atender una alerta SOS
  Como conductor
  Quiero activar una alerta SOS durante un viaje
  Para solicitar atención prioritaria ante una emergencia

  Escenario: Activar SOS durante un viaje activo
    Dado un viaje activo
    Y existen estudiantes a bordo
    Y existe una posición GPS disponible
    Cuando el conductor activa una alerta SOS
    Entonces debe crearse una alerta SOS activa
    Y debe generarse un evento SosRaised
    Y el evento debe contener la posición actual
    Y debe contener los estudiantes a bordo
    Y debe contener el identificador del vehículo

  Escenario: Activar SOS sin una posición GPS disponible
    Dado un viaje activo
    Y no existe una posición GPS disponible
    Cuando el conductor activa una alerta SOS
    Entonces la alerta SOS debe registrarse
    Y la posición del evento SosRaised debe quedar vacía

  Escenario: Evitar una segunda alerta mientras existe un SOS activo
    Dado un viaje activo
    Y existe una alerta SOS activa
    Cuando el conductor intenta activar otra alerta SOS
    Entonces la segunda operación no debe aplicarse
    Y debe permanecer una sola alerta SOS registrada

  Escenario: Rechazar SOS cuando el viaje no ha iniciado
    Dado un viaje que todavía no ha iniciado
    Cuando el conductor intenta activar una alerta SOS
    Entonces la acción debe rechazarse con NotActive

  Escenario: Resolver una alerta SOS activa
    Dado un viaje activo
    Y existe una alerta SOS activa
    Cuando un operador autorizado resuelve la alerta
    Entonces ya no debe existir una alerta SOS activa
    Y debe generarse un evento SosResolved
    Y debe registrarse el operador que resolvió la alerta

  Escenario: Rechazar resolución cuando no existe un SOS activo
    Dado un viaje activo sin una alerta SOS activa
    Cuando un operador intenta resolver una alerta SOS
    Entonces la acción debe rechazarse con NoActiveSos