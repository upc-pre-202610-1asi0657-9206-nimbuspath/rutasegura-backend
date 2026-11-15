namespace RutaSegura.TripTracking.Api.Resources;

// Cuerpos de las peticiones REST (JSON camelCase). Los enums viajan como texto:
// incidentType "HeavyTraffic" o "HEAVY_TRAFFIC", type "Boarding", status "InProgress".

/// <summary>Coordenada opcional que la app adjunta a una acción (si el GPS tiene señal).</summary>
public sealed record PositionResource(double Latitude, double Longitude);

/// <summary>Acción del conductor sin más datos: iniciar / finalizar / deshacer.</summary>
public sealed record DriverActionResource(Guid ClientEventId);

public sealed record PassengerActionResource(Guid StudentId, Guid ClientEventId, DateTimeOffset? OccurredAt = null, PositionResource? Position = null);

public sealed record AbsenceResource(Guid StudentId, Guid ClientEventId, DateTimeOffset? OccurredAt = null);

public sealed record GuardianAbsenceResource(Guid StudentId, string? Note = null);

public sealed record IncidentResource(
    Guid ClientEventId, string IncidentType, string? Detail = null, bool NotifyGuardians = false,
    DateTimeOffset? OccurredAt = null, PositionResource? Position = null);

public sealed record SosResource(Guid ClientEventId, DateTimeOffset? OccurredAt = null, PositionResource? Position = null);

public sealed record CancelTripResource(string Reason);

public sealed record ScheduleTripResource(Guid RouteId, DateOnly ServiceDate);

public sealed record GpsReadingResource(
    double Latitude, double Longitude, DateTimeOffset RecordedAt, double AccuracyMeters,
    double? SpeedKmh = null, double? HeadingDegrees = null);

/// <summary>Lote de lecturas GPS (1 cada 3-5 s en línea; todo lo acumulado al recuperar señal).</summary>
public sealed record RecordPositionsResource(Guid TripId, IReadOnlyList<GpsReadingResource> Readings);

public sealed record OfflineEventResource(
    Guid ClientEventId, string Type, DateTimeOffset OccurredAt, Guid? StudentId = null, PositionResource? Position = null,
    string? IncidentType = null, string? Detail = null, bool NotifyGuardians = false);

/// <summary>Cola local de la app enviada al recuperar conexión (≤ 200 eventos).</summary>
public sealed record SyncOfflineEventsResource(IReadOnlyList<OfflineEventResource> Events);
