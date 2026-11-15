using RabbitMQ.Client;

namespace RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

public sealed class NotificationRabbitMqConnection(NotificationRabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;
            if (_connection is not null) await _connection.DisposeAsync();
            var factory = new ConnectionFactory
            {
                HostName = options.HostName, Port = options.Port, VirtualHost = options.VirtualHost,
                UserName = options.UserName, Password = options.Password,
                ClientProvidedName = "notification-consumer", AutomaticRecoveryEnabled = false,
                ConsumerDispatchConcurrency = 1, RequestedHeartbeat = TimeSpan.FromSeconds(30),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                Ssl = new SslOption { Enabled = options.UseTls, ServerName = options.TlsServerName ?? options.HostName }
            };
            // El worker recupera conexion y topologia completos; nunca reutiliza delivery tags de otro canal.
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
