namespace RutaSegura.TripTracking.Domain.Common;

/// <summary>
/// Códigos de error estables del bounded context. Los consume el cliente móvil y la API
/// los mapea a HTTP (ver Etapa 5). Mantener alineado con 5.1.1.
/// </summary>
public static class TripErrors
{
    public const string InvalidTransition = "TRIP_INVALID_TRANSITION";
    public const string NotActive = "TRIP_NOT_ACTIVE";
    public const string DriverMismatch = "TRIP_DRIVER_MISMATCH";
    public const string StartTooEarly = "TRIP_START_TOO_EARLY";
    public const string HasPendingStudents = "TRIP_HAS_PENDING_STUDENTS";
    public const string HasStudentsOnBoard = "TRIP_HAS_STUDENTS_ON_BOARD";
    public const string HasActiveSos = "TRIP_HAS_ACTIVE_SOS";
    public const string InvalidDefinition = "TRIP_INVALID_DEFINITION";
    public const string StudentNotInRoster = "STUDENT_NOT_IN_ROSTER";
    public const string PassengerInvalidTransition = "PASSENGER_INVALID_TRANSITION";
    public const string EventTimestampInvalid = "EVENT_TIMESTAMP_INVALID";
    public const string IncidentDetailRequired = "INCIDENT_DETAIL_REQUIRED";
    public const string IncidentDetailTooLong = "INCIDENT_DETAIL_TOO_LONG";
    public const string NoActiveSos = "TRIP_NO_ACTIVE_SOS";
    public const string CancellationReasonRequired = "TRIP_CANCELLATION_REASON_REQUIRED";
}

public static class TrackingErrors
{
    public const string RequiresActiveTrip = "TRACKING_REQUIRES_ACTIVE_TRIP";
    public const string InvalidCoordinates = "TRACKING_INVALID_COORDINATES";
    public const string InvalidReading = "TRACKING_INVALID_READING";
}
