using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Replicas;
using RutaSegura.TripTracking.Application.Trips.Commands;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Integration.Tests.Infrastructure;

/// <summary>
/// Disposable Test Environment (5.1): levanta la API real (Program) contra una base MySQL creada para la clase
/// de pruebas y la elimina al terminar. Reloj controlado (SettableClock) fijo en "lunes 5 de octubre 2026, 06:40 Lima".
/// RabbitMQ deshabilitado (canal en vivo en proceso); las pruebas de broker usan su propio fixture.
/// </summary>
public sealed class TripTrackingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecret = "integration-tests-secret-0123456789-abcdefghij";
    public const string JwtIssuer = "rutasegura-iam";
    public const string JwtAudience = "rutasegura-api";
    public static readonly TimeSpan Lima = TimeSpan.FromHours(-5);
    public static readonly DateTimeOffset Monday0640 = new(2026, 10, 5, 6, 40, 0, Lima);

    private readonly string _serverConnection =
        Environment.GetEnvironmentVariable("TRIPTRACKING_IT_MYSQL")
        ?? "server=localhost;port=3306;user=rutasegura;password=rutasegura";

    private readonly string _database = $"tt_it_{Guid.NewGuid():N}"[..20];

    public SettableClock Clock { get; } = new(Monday0640);

    public bool MySqlAvailable { get; private set; }

    public string ConnectionString => $"{_serverConnection};database={_database}";

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_serverConnection);
            await connection.OpenAsync();
            MySqlAvailable = true;
        }
        catch (Exception)
        {
            MySqlAvailable = false;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (MySqlAvailable)
        {
            try
            {
                await using var connection = new MySqlConnection(_serverConnection);
                await connection.OpenAsync();
                await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{_database}`", connection);
                await drop.ExecuteNonQueryAsync();
            }
            catch (Exception)
            {
                // limpieza best-effort
            }
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TripTracking", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Jwt:Secret", JwtSecret);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        // Estas pruebas cubren el flujo del viaje; la verificación de cuentas en Iam se prueba en Security/.
        builder.UseSetting("Iam:AccountValidation:Enabled", "false");
        builder.UseSetting("Persistence:MigrateOnStartup", "true");
        builder.UseSetting("RabbitMq:Enabled", "false");
        builder.UseSetting("Scheduling:Enabled", "false");
        builder.UseSetting("Seed:Demo", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Sale de la prueba como "omitida" si no hay MySQL (no es un fallo del código).</summary>
    public void RequireMySql() =>
        Assert.SkipUnless(MySqlAvailable, "MySQL no disponible (define TRIPTRACKING_IT_MYSQL o levanta el docker-compose).");

    public HttpClient CreateClientAs(string role, Guid userId, Guid? organizationId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role, userId, organizationId));
        return client;
    }

    /// <summary>Mismo formato que el TokenService de Iam: nameidentifier, role (ADMIN/DRIVER/PARENT), organizationId, iss y aud.</summary>
    public static string Token(string role, Guid userId, Guid? organizationId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (organizationId is { } org) claims.Add(new Claim("organizationId", org.ToString()));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = JwtIssuer,
            Audience = JwtAudience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256Signature)
        });
    }

    public async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.FirstError.Code}: {result.FirstError.Message}" : null);
        return result.Value;
    }

    /// <summary>Crea una ruta propia (ids únicos) y programa su viaje del lunes 5/10 a las 06:40.</summary>
    public async Task<SeededTrip> SeedTripAsync(Guid? organizationId = null, TripDirection direction = TripDirection.Pickup)
    {
        var trip = new SeededTrip(organizationId ?? Guid.NewGuid());
        var route = new RouteSnapshot(trip.RouteId, "Ruta IT", direction, trip.DriverId, Guid.NewGuid(), new TimeOnly(6, 40),
            "America/Lima", Enum.GetValues<DayOfWeek>(),
            [
                new RouteStopSnapshot(trip.Stop1, 1, "Av. Primavera 1240", -12.1000, -77.0000),
                new RouteStopSnapshot(trip.School, 2, "Colegio San Agustín", -12.0800, -77.0000)
            ],
            [new RouteStudentSnapshot(trip.Student1, trip.Stop1), new RouteStudentSnapshot(trip.Student2, trip.Stop1)],
            trip.OrganizationId);

        await SendAsync(new UpsertRouteSnapshotCommand(route));
        await SendAsync(new LinkGuardianStudentCommand(trip.GuardianId, trip.Student1));
        trip.TripId = await SendAsync(new ScheduleTripCommand(trip.RouteId, new DateOnly(2026, 10, 5)));
        return trip;
    }
}

/// <summary>
/// Reloj de negocio controlado que puede ir hacia atrás (cada prueba fija su hora). Los temporizadores
/// siguen siendo los del sistema, así los workers en segundo plano no se congelan.
/// </summary>
public sealed class SettableClock(DateTimeOffset start) : TimeProvider
{
    private long _utcTicks = start.UtcTicks;

    public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
}

public sealed class SeededTrip(Guid organizationId)
{
    public Guid OrganizationId { get; } = organizationId;
    public Guid RouteId { get; } = Guid.NewGuid();
    public Guid DriverId { get; } = Guid.NewGuid();
    public Guid GuardianId { get; } = Guid.NewGuid();
    public Guid Stop1 { get; } = Guid.NewGuid();
    public Guid School { get; } = Guid.NewGuid();
    public Guid Student1 { get; } = Guid.NewGuid();
    public Guid Student2 { get; } = Guid.NewGuid();
    public Guid TripId { get; set; }
}
