using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Security;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NOTIFICATION_TEST_MYSQL")))
            Skip = "Configura NOTIFICATION_TEST_MYSQL con un servidor MySQL 8.4 LOCAL y permisos para crear bases temporales.";
    }
}

internal static class TestSecrets
{
    internal static readonly string EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    internal static readonly IPushTokenProtector Protector = new AesGcmPushTokenProtector(EncryptionKey);
}

public abstract class MySqlTestContext : IAsyncLifetime
{
    protected readonly Guid Organization = Guid.NewGuid();
    protected readonly Guid OtherOrganization = Guid.NewGuid();
    protected readonly Guid Guardian = Guid.NewGuid();
    protected readonly Guid Student = Guid.NewGuid();
    protected readonly Guid Trip = Guid.NewGuid();
    protected readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _database = "notification_test_" + Guid.NewGuid().ToString("N");
    private string _serverConnection = string.Empty;
    protected string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_MYSQL")
            ?? throw new InvalidOperationException("Falta NOTIFICATION_TEST_MYSQL.");
        var connection = new MySqlConnectionStringBuilder(configured);
        if (connection.Server is not ("127.0.0.1" or "localhost"))
            throw new InvalidOperationException("Las pruebas solo crean bases efimeras en un servidor local aislado.");
        connection.Database = string.Empty;
        _serverConnection = connection.ConnectionString;
        await using var server = new MySqlConnection(_serverConnection);
        await server.OpenAsync(Ct);
        await using var create = server.CreateCommand();
        create.CommandText = $"CREATE DATABASE `{_database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin";
        await create.ExecuteNonQueryAsync(Ct);
        connection.Database = _database;
        ConnectionString = connection.ConnectionString;
        await using var context = OpenContext();
        await context.Database.MigrateAsync(Ct);
    }

    protected NotificationDbContext OpenContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseMySQL(ConnectionString).Options, TestSecrets.Protector);

    protected ServiceProvider Services(RecipientDirectory directory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        services.AddNotificationApplication();
        services.AddNotificationPersistence(ConnectionString, TestSecrets.EncryptionKey);
        services.AddScoped<INotificationRecipientDirectory>(_ => directory);
        services.AddScoped<ITripNotificationContext>(_ => directory);
        services.AddScoped<INotificationUserContext>(_ => new TrustedUser(Guardian, Organization));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    protected NotificationEntity Notice(Guid? source = null, Guid? student = null, Guid? organization = null,
        Guid? recipient = null, DateTimeOffset? createdAt = null, Guid? announcement = null) =>
        NotificationEntity.Create(organization ?? Organization, recipient ?? Guardian, source ?? Guid.NewGuid(),
            "trip.student-boarded.v1", "integration-test", NotificationCategory.Boarding,
            NotificationContent.Create("Aviso", "Contenido autorizado"), Now, createdAt ?? Now,
            tripId: Trip, studentId: student, announcementId: announcement);

    protected ProcessedNotificationEvent Processed(Guid? source = null, string consumer = "notification.trip-events.v1") =>
        ProcessedNotificationEvent.Record(consumer, source ?? Guid.NewGuid(), Organization, "trip.student-boarded.v1",
            new string('A', 64), Now, Now, 1, null);

    public async ValueTask DisposeAsync()
    {
        if (string.IsNullOrEmpty(_serverConnection)) return;
        // Solo la base creada por esta instancia; nunca se borra la base suministrada por el usuario.
        if (!System.Text.RegularExpressions.Regex.IsMatch(_database, "^notification_test_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Nombre de base de prueba no valido.");
        MySqlConnection.ClearAllPools();
        await using var server = new MySqlConnection(_serverConnection);
        await server.OpenAsync(CancellationToken.None);
        await using var drop = server.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class TrustedUser(Guid user, Guid organization) : INotificationUserContext
    {
        public NotificationUser? Current => new(user, organization, CanPublishAnnouncements: true);
    }
}

public sealed class RecipientDirectory(Guid organization, Guid guardian, Guid student) : INotificationRecipientDirectory, ITripNotificationContext
{
    public bool Unavailable { get; set; }
    public Task<IReadOnlyList<NotificationRecipient>> ForStudentsAsync(Guid organizationId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException("Directorio de prueba no disponible.");
        return Task.FromResult<IReadOnlyList<NotificationRecipient>>(organizationId == organization && studentIds.Contains(student)
            ? [new(organization, guardian, student)] : []);
    }
    public Task<IReadOnlyList<NotificationRecipient>> OperatorsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>([]);
    public Task<Result<IReadOnlyList<NotificationRecipient>>> ForAnnouncementAsync(Guid organizationId, Guid? routeId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<NotificationRecipient>>.Success([new(organization, guardian)]));
    public Task<IReadOnlyCollection<Guid>> CurrentStudentLinksAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(organizationId == organization && userId == guardian ? [student] : []);
    public Task<bool> IsActiveAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken) => Task.FromResult(true);
}
