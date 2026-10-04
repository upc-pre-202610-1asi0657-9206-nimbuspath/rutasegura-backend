namespace RutaSegura.TripTracking.Domain.Tracking;

/// <summary>
/// Umbrales del filtrado de telemetría y de detección de paradas.
/// Centralizados para documentarlos en el informe y ajustarlos con datos reales del piloto.
/// </summary>
public static class TrackingPolicy
{
    /// <summary>Lecturas con precisión peor que esto se descartan (el mapa del tutor "saltaría").</summary>
    public const double MaxAccuracyMeters = 100;

    /// <summary>Velocidad implícita máxima entre dos puntos: más que esto es un salto de GPS.</summary>
    public const double MaxPlausibleSpeedKmh = 150;

    /// <summary>Tolerancia para relojes de celular adelantados.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>Radio para considerar que el bus llegó a una parada.</summary>
    public const double StopArrivalRadiusMeters = 60;

    /// <summary>Radio para avisar "el bus se acerca" a las familias de esa parada.</summary>
    public const double StopApproachRadiusMeters = 800;
}

/// <summary>Estimación simple de llegada; se reemplazará por el ACL de OpenRouteService si se requiere precisión.</summary>
public static class EtaEstimator
{
    /// <summary>Velocidad urbana promedio en Lima para buses escolares cuando el GPS no reporta velocidad útil.</summary>
    public const double DefaultUrbanSpeedKmh = 20;

    private const double MinUsefulSpeedKmh = 5;

    public static int EstimateMinutes(double distanceMeters, double? currentSpeedKmh)
    {
        if (distanceMeters <= 0) return 0;

        var speed = currentSpeedKmh is { } s && s >= MinUsefulSpeedKmh ? s : DefaultUrbanSpeedKmh;
        var minutes = distanceMeters / 1000d / speed * 60d;
        return (int)Math.Ceiling(minutes);
    }
}
