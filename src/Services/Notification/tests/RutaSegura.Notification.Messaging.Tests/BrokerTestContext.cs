using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using RabbitMQ.Client;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Security;
using RutaSegura.TripTracking.Infrastructure.Persistence;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class BrokerFactAttribute : FactAttribute
{
    public BrokerFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_MYSQL")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_PORT")))
            Skip = "Ejecuta scripts/Test-NotificationMessaging.ps1 para provisionar MySQL y RabbitMQ efimeros.";
    }
}

public abstract class BrokerTestContext : IAsyncLifetime
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
    protected readonly string EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    protected readonly TestDirectory Directory = new();
    protected readonly string Database = "notification_test_" + Guid.NewGuid().ToString("N");
    protected readonly string TripDatabase = "tracking_test_" + Guid.NewGuid().ToString("N");
    private string _serverConnection = "";
    protected string NotificationConnection = "", TripConnection = "";
    protected NotificationRabbitMqOptions Options = null!;
    protected ServiceProvider Services = null!;
    protected NotificationRabbitMqConsumer Worker = null!;
    protected IConnection Broker = null!;
    protected IChannel Channel = null!;

    public async ValueTask InitializeAsync()
    {
        var sql = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_MYSQL"));
        if (sql.Server is not ("localhost" or "127.0.0.1")) throw new InvalidOperationException("Pruebas solo sobre servidor local aislado.");
        sql.Database = "";
        _serverConnection = sql.ConnectionString;
        await using (var server = new MySqlConnection(_serverConnection))
        {
            await server.OpenAsync(Ct);
            foreach (var name in new[] { Database, TripDatabase })
            {
                await using var create = server.CreateCommand();
                create.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin";
                await create.ExecuteNonQueryAsync(Ct);
            }
        }
        sql.Database = Database; NotificationConnection = sql.ConnectionString;
        sql.Database = TripDatabase; TripConnection = sql.ConnectionString;
        await using (var db = OpenNotification()) await db.Database.MigrateAsync(Ct);
        await using (var db = OpenTrip()) await db.Database.MigrateAsync(Ct);

        Options = new()
        {
            HostName = "127.0.0.1", Port = int.Parse(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_PORT")!),
            UserName = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_USER")!,
            Password = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_RABBITMQ_PASSWORD")!,
            Queue = "notification.test." + Guid.NewGuid().ToString("N"),
            EventsExchange = "tracking.test." + Guid.NewGuid().ToString("N"),
            RetryDelaysMilliseconds = [50, 100], ReconnectDelay = TimeSpan.FromMilliseconds(100)
        };
        var factory = new ConnectionFactory { HostName = Options.HostName, Port = Options.Port, UserName = Options.UserName, Password = Options.Password };
        Broker = await factory.CreateConnectionAsync(Ct);
        Channel = await Broker.CreateChannelAsync(new CreateChannelOptions(true, true), Ct);
        // El productor puede declarar primero; Notification debe coexistir sin error 406.
        await RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq.RabbitMqTopology.DeclareEventsExchangeAsync(Channel, Options.EventsExchange, Ct);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationApplication();
        services.AddNotificationPersistence(NotificationConnection, EncryptionKey);
        services.AddSingleton<INotificationRecipientDirectory>(Directory);
        services.AddSingleton<ITripNotificationContext>(Directory);
        services.AddSingleton<INotificationUserContext>(Directory);
        services.AddNotificationRabbitMq(Options);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Worker = Services.GetRequiredService<NotificationRabbitMqConsumer>();
        await Worker.StartAsync(Ct);
        using var ready = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        ready.CancelAfter(TimeSpan.FromSeconds(20));
        await Worker.WaitUntilConsumingAsync(ready.Token);
    }

    protected NotificationDbContext OpenNotification() => new(new DbContextOptionsBuilder<NotificationDbContext>().UseMySQL(NotificationConnection).Options,
        new AesGcmPushTokenProtector(EncryptionKey));
    protected TripTrackingDbContext OpenTrip() => new(new DbContextOptionsBuilder<TripTrackingDbContext>().UseMySQL(TripConnection).Options);

    protected async Task Publish(RutaSegura.Notification.Infrastructure.Messaging.Inbound.NotificationIncomingMessage message)
    {
        await Channel.BasicPublishAsync(Options.EventsExchange, message.RoutingKey, true, new BasicProperties
        {
            MessageId = message.MessageId, Type = message.MessageType, CorrelationId = message.CorrelationId,
            ContentType = message.ContentType, DeliveryMode = DeliveryModes.Persistent, Headers = message.Headers,
            Timestamp = new AmqpTimestamp(message.UnixTimestamp)
        }, message.Body, Ct);
    }

    protected static async Task Eventually(Func<Task<bool>> assertion)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!await assertion()) await Task.Delay(50, timeout.Token);
    }

    protected async Task<BasicGetResult> DeadLetter()
    {
        BasicGetResult? result = null;
        await Eventually(async () => (result = await Channel.BasicGetAsync(Options.ErrorQueue, true, Ct)) is not null);
        return result!;
    }

    public async ValueTask DisposeAsync()
    {
        if (Worker is not null) await Worker.StopAsync(CancellationToken.None);
        if (Services is not null) await Services.DisposeAsync();
        if (Channel is not null && Channel.IsOpen)
        {
            // Nombres aleatorios creados por ESTA instancia, no colas del entorno del usuario.
            foreach (var queue in new[] { Options.Queue, Options.ErrorQueue, Options.EventsExchange + ".unrouted" }
                         .Concat(Enumerable.Range(1, Options.RetryDelaysMilliseconds.Length).Select(Options.RetryQueue)))
                await Channel.QueueDeleteAsync(queue, false, false, cancellationToken: CancellationToken.None);
            foreach (var exchange in new[] { Options.EventsExchange, Options.EventsExchange + ".unrouted", Options.ErrorExchange, Options.ReturnExchange, Options.RetryExchange })
                await Channel.ExchangeDeleteAsync(exchange, false, cancellationToken: CancellationToken.None);
        }
        if (Channel is not null) await Channel.DisposeAsync();
        if (Broker is not null) await Broker.DisposeAsync();
        if (string.IsNullOrEmpty(_serverConnection)) return;
        MySqlConnection.ClearAllPools();
        await using var server = new MySqlConnection(_serverConnection);
        await server.OpenAsync(CancellationToken.None);
        foreach (var name in new[] { Database, TripDatabase })
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^(notification|tracking)_test_[0-9a-f]{32}$")) throw new InvalidOperationException();
            await using var drop = server.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`";
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}

public sealed class TestDirectory : INotificationRecipientDirectory, ITripNotificationContext, INotificationUserContext
{
    public int FailuresRemaining { get; set; }
    public int Calls { get; private set; }
    public bool Active { get; set; } = true;
    public NotificationUser? Current => null;
    public Task<IReadOnlyList<NotificationRecipient>> ForStudentsAsync(Guid organizationId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        Calls++;
        if (FailuresRemaining-- > 0) throw new HttpRequestException("Directorio temporalmente no disponible (doble de prueba).");
        return Task.FromResult<IReadOnlyList<NotificationRecipient>>(studentIds.Select(s => new NotificationRecipient(organizationId, MessageSamples.Guardian, s)).ToArray());
    }
    public Task<IReadOnlyList<NotificationRecipient>> OperatorsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>([new(organizationId, MessageSamples.Operator)]);
    public Task<bool> IsActiveAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken) => Task.FromResult(Active);
    public Task<Result<IReadOnlyList<NotificationRecipient>>> ForAnnouncementAsync(Guid organizationId, Guid? routeId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyCollection<Guid>> CurrentStudentLinksAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
