namespace RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

public sealed class NotificationRabbitMqOptions
{
    public const string Section = "NotificationRabbitMq";
    public bool Enabled { get; set; }
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string? TlsServerName { get; set; }
    public string EventsExchange { get; set; } = "trip-tracking.events";
    public string Queue { get; set; } = "notification.trip-events.v1";
    public ushort PrefetchCount { get; set; } = 20;
    public int MaxMessageBytes { get; set; } = 65536;
    public int[] RetryDelaysMilliseconds { get; set; } = [1000, 5000, 30000];
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
    public string RetryExchange => Queue + ".retry";
    public string ReturnExchange => Queue + ".return";
    public string ErrorExchange => Queue + ".errors";
    public string ErrorQueue => Queue + ".dlq";
    public string RetryQueue(int attempt) => Queue + ".retry." + RetryDelaysMilliseconds[attempt - 1];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName) || Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(VirtualHost) ||
            string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(EventsExchange) || string.IsNullOrWhiteSpace(Queue) ||
            EventsExchange.Length > 180 || Queue.Length > 180 || PrefetchCount is < 1 or > 1000 ||
            MaxMessageBytes is < 1024 or > 1048576 || ReconnectDelay < TimeSpan.FromMilliseconds(10) ||
            RetryDelaysMilliseconds is null || RetryDelaysMilliseconds.Length > 10 ||
            RetryDelaysMilliseconds.Any(delay => delay < 10 || delay > 3600000) ||
            RetryDelaysMilliseconds.Distinct().Count() != RetryDelaysMilliseconds.Length ||
            (UseTls && string.IsNullOrWhiteSpace(TlsServerName)))
            throw new ArgumentException("Configuracion de RabbitMQ de Notification invalida; revisa conexion, topologia y limites.");
    }
}
