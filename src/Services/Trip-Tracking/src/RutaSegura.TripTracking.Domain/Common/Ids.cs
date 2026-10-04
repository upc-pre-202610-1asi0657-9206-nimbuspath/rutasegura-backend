namespace RutaSegura.TripTracking.Domain.Common;

// Identificadores fuertemente tipados: evitan pasar un StudentId donde se espera un StopId.
// Los IDs de otros bounded contexts (Route, Stop, Student, Driver, Vehicle, Guardian) son
// referencias por identidad: este servicio nunca carga esos agregados, solo guarda su Id.

public readonly record struct TripId(Guid Value)
{
    public static TripId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

public readonly record struct RouteId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct StopId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct StudentId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct DriverId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct VehicleId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct GuardianId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct IncidentId(Guid Value)
{
    public static IncidentId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// UUID que genera la app del conductor por cada acción (abordaje, incidente, SOS…).
/// Permite reintentos y sincronización offline sin duplicar registros (PT-04 / PT-09).
/// </summary>
public readonly record struct ClientEventId(Guid Value)
{
    public static ClientEventId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
