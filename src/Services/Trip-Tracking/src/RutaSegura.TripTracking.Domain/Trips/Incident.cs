using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;

namespace RutaSegura.TripTracking.Domain.Trips;

public sealed class Incident
{
    public const int MaxDetailLength = 500;

    public IncidentId Id { get; private set; }
    public IncidentType Type { get; private set; }
    public string? Detail { get; private set; }
    public bool NotifyGuardians { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public GeoPosition? Position { get; private set; }

    private Incident() { } // EF Core

    internal Incident(IncidentId id, IncidentType type, string? detail, bool notifyGuardians, DateTimeOffset occurredAt, GeoPosition? position)
    {
        Id = id;
        Type = type;
        Detail = detail;
        NotifyGuardians = notifyGuardians;
        OccurredAt = occurredAt;
        Position = position;
    }

    /// <summary>Incidentes que retrasan el viaje y lo pasan a estado Delayed.</summary>
    public static bool CausesDelay(IncidentType type) =>
        type is IncidentType.HeavyTraffic or IncidentType.RouteDeviation or IncidentType.VehicleFailure or IncidentType.Accident;
}

/// <summary>Alerta de emergencia. Queda activa hasta que la empresa la resuelve.</summary>
public sealed class SosAlert
{
    public Guid Id { get; private set; }
    public DateTimeOffset RaisedAt { get; private set; }
    public GeoPosition? Position { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedBy { get; private set; }

    private SosAlert() { } // EF Core

    internal SosAlert(DateTimeOffset raisedAt, GeoPosition? position)
    {
        Id = Guid.CreateVersion7();
        RaisedAt = raisedAt;
        Position = position;
    }

    public bool IsActive => ResolvedAt is null;

    internal void Resolve(Guid resolvedBy, DateTimeOffset at)
    {
        ResolvedAt = at;
        ResolvedBy = resolvedBy;
    }
}
