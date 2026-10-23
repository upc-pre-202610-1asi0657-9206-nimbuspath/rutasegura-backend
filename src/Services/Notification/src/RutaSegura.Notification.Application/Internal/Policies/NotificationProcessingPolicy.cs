using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.Policies;

/// <summary>Politica configurable para no convertir proximidades antiguas de una sincronizacion offline en alertas actuales.</summary>
public sealed class NotificationProcessingPolicy
{
    public static NotificationProcessingPolicy Default { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
    public NotificationProcessingPolicy(TimeSpan proximityLifetime, TimeSpan? maxFutureClockSkew = null)
    {
        if (proximityLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(proximityLifetime));
        if (maxFutureClockSkew < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxFutureClockSkew));
        ProximityLifetime = proximityLifetime;
        MaxFutureClockSkew = maxFutureClockSkew ?? TimeSpan.FromSeconds(30);
    }
    public TimeSpan ProximityLifetime { get; }
    public TimeSpan MaxFutureClockSkew { get; }
}
