namespace RutaSegura.Notification.Infrastructure.Channels;

public sealed class NotificationChannelOptions
{
    public bool Enabled { get; set; }
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 4;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    public void Validate()
    {
        if (BatchSize is < 1 or > 500 || MaxAttempts is < 1 or > 10 || MaxConcurrency is < 1 or > 16 || PollingInterval < TimeSpan.FromMilliseconds(50))
            throw new InvalidOperationException("NotificationChannels requiere lote, intentos e intervalo validos.");
    }
}

public sealed class NotificationFcmOptions
{
    public bool Enabled { get; set; }
    public string ProjectId { get; set; } = "";
}
