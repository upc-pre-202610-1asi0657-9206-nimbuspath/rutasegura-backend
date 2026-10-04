using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;

namespace RutaSegura.TripTracking.Domain.Trips;

/// <summary>Datos de una parada copiados desde la ruta (Administration) al programar el viaje.</summary>
public sealed record TripStopDefinition(StopId StopId, int Sequence, string Name, GeoPosition Position);

/// <summary>Alumno asignado al viaje y la parada donde sube (recojo) o baja (retorno).</summary>
public sealed record TripPassengerDefinition(StudentId StudentId, StopId StopId);

/// <summary>Parada dentro del viaje. Snapshot inmutable de la ruta + estado de llegada.</summary>
public sealed class TripStop
{
    public StopId StopId { get; private set; }
    public int Sequence { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public GeoPosition Position { get; private set; } = null!;
    public DateTimeOffset? ReachedAt { get; private set; }
    public bool ApproachNotified { get; private set; }

    private TripStop() { } // EF Core

    internal TripStop(TripStopDefinition definition)
    {
        StopId = definition.StopId;
        Sequence = definition.Sequence;
        Name = definition.Name;
        Position = definition.Position;
    }

    public bool IsReached => ReachedAt is not null;

    internal void MarkReached(DateTimeOffset at) => ReachedAt ??= at;

    internal void MarkApproachNotified() => ApproachNotified = true;
}
