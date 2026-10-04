namespace RutaSegura.TripTracking.Domain.Trips;

public enum TripStatus
{
    Scheduled,
    InProgress,
    Delayed,
    Completed,
    Cancelled
}

/// <summary>Recojo (casas → colegio) o retorno (colegio → casas).</summary>
public enum TripDirection
{
    Pickup,
    Return
}

public enum PassengerStatus
{
    Pending,
    Boarded,
    Alighted,
    Absent
}

public enum AbsenceSource
{
    /// <summary>El conductor marcó "No vino" en la parada.</summary>
    Driver,

    /// <summary>El apoderado avisó antes que el alumno no asiste.</summary>
    Guardian
}

/// <summary>Opciones de la pantalla "Reportar incidente" de la app del conductor.</summary>
public enum IncidentType
{
    HeavyTraffic,
    RouteDeviation,
    VehicleFailure,
    StudentUnwell,
    Accident,
    Other
}
