namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Maintenance;

public sealed class NotificationRetentionOptions
{
    public bool Enabled { get; set; }
    public bool DryRun { get; set; } = true;
    public int ReadNoticesDays { get; set; }
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 10;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public void Validate()
    {
        if (!Enabled) return;
        if (ReadNoticesDays is < 1 or > 36500 || BatchSize is < 1 or > 500 || MaxBatchesPerRun is < 1 or > 100 ||
            Interval < TimeSpan.FromMinutes(1) || Interval > TimeSpan.FromDays(1))
            throw new InvalidOperationException("La retencion requiere politica explicita de dias, lote y frecuencia validos.");
    }
}
