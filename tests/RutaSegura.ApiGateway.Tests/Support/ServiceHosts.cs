extern alias monolith;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Api.Security;
using monolith::Monolith.Iam.Application.Internal.OutboundServices;
using monolith::Monolith.Iam.Infrastructure.Persistence.EFC.Seeding;
using monolith::Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using MonolithProgram = monolith::Program;

namespace RutaSegura.ApiGateway.Tests.Support;

/// <summary>Valores compartidos por los dos hosts, como en docker-compose (x-jwt).</summary>
public static class SharedSecurity
{
    public const string Secret = "iam-trip-tracking-integration-secret-0123456789abcdef";
    public const string Issuer = "rutasegura-iam";
    public const string Audience = "rutasegura-api";
    public const string ProvisioningKey = "iam-trip-tracking-integration-provisioning-0123456789";
}

/// <summary>
/// El monolito real (Program.cs con Iam y Administration) sobre SQLite en memoria con el mismo modelo EF y las
/// cuentas demo de <c>IamSeeder</c>. Es quien emite los tokens.
/// </summary>
public sealed class IamHost : WebApplicationFactory<MonolithProgram>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public IamHost() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused-in-tests");
        builder.UseSetting("TokenSettings:Secret", SharedSecurity.Secret);
        builder.UseSetting("TokenSettings:Issuer", SharedSecurity.Issuer);
        builder.UseSetting("TokenSettings:Audience", SharedSecurity.Audience);
        builder.UseSetting("Iam:Provisioning:ApiKey", SharedSecurity.ProvisioningKey);
        builder.UseSetting("Administration:Messaging:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
        IamSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<IHashingService>()).GetAwaiter().GetResult();
        return host;
    }

    /// <summary><c>POST /api/v1/users/sign-in</c>: el token que usa la app móvil.</summary>
    public async Task<string> SignInAsync(string email, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/users/sign-in", new { email, password }, Json,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return body.GetProperty("token").GetString()!;
    }

    public HttpClient As(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

/// <summary>
/// Trip &amp; Tracking real (autenticación, políticas, hub) configurado como en docker-compose, con
/// <c>Iam:BaseUrl</c> apuntando al <see cref="IamHost"/>: la verificación de cuentas viaja por HTTP al monolito.
/// Los casos de uso se sustituyen por <see cref="RecordingDispatcher"/> (no hace falta MySQL).
/// </summary>
public sealed class TripTrackingHost(IamHost iam, IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public RecordingDispatcher Dispatcher { get; } = new();

    /// <summary>Simula que el monolito se cae (la conexión hacia Iam falla).</summary>
    public SwitchableHandler IamLink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:TripTracking"] = "server=127.0.0.1;port=1;database=unused;user=unused;password=unused",
            ["ConnectionStrings:Redis"] = "",
            ["Jwt:Secret"] = SharedSecurity.Secret,
            ["Jwt:Issuer"] = SharedSecurity.Issuer,
            ["Jwt:Audience"] = SharedSecurity.Audience,
            ["Iam:BaseUrl"] = iam.Server.BaseAddress.ToString(),
            ["Iam:AccountValidation:Enabled"] = "true",
            ["Iam:AccountValidation:UnavailableCacheDuration"] = "00:00:00",
            ["Persistence:MigrateOnStartup"] = "false",
            ["RabbitMq:Enabled"] = "false",
            ["Scheduling:Enabled"] = "false",
            ["Seed:Demo"] = "false",
            ["DevTokens:Enabled"] = "false"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>()) settings[key] = value;
        foreach (var (key, value) in settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDispatcher>();
            services.AddSingleton<IDispatcher>(Dispatcher);
            // Trip & Tracking llega a Iam por HTTP: aquí el "cable" es el TestServer del monolito.
            services.AddHttpClient(HttpIamAccountVerifier.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => IamLink.Connect(iam.Server.CreateHandler()));
        });
    }

    public HttpClient As(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Conexión hacia Iam que se puede cortar para simular la caída del monolito.</summary>
public sealed class SwitchableHandler : DelegatingHandler
{
    private int _calls;

    public bool Down { get; set; }
    public int Calls => Volatile.Read(ref _calls);

    public SwitchableHandler Connect(HttpMessageHandler inner)
    {
        InnerHandler ??= inner;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Down) throw new HttpRequestException("Connection refused (monolito detenido)");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Reemplaza a los casos de uso de Trip &amp; Tracking: registra lo que llega y responde éxito vacío.</summary>
public sealed class RecordingDispatcher : IDispatcher
{
    public ConcurrentQueue<object> Received { get; } = new();

    public T Last<T>() => Received.OfType<T>().Last();

    public Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        Received.Enqueue(command);
        return Task.FromResult(Result<TResponse>.Success(default!));
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        Received.Enqueue(query);
        return Task.FromResult(Result<TResponse>.Success(default!));
    }
}

/// <summary>Cuentas que crea <c>IamSeeder</c> en "Transportes Escolares Lima Norte".</summary>
public static class SeedAccounts
{
    public static readonly Guid Organization = IamSeeder.SeedOrganizationId;
    public const string AdminEmail = "admin@saferoute.pe";
    public const string AdminPassword = "admin123";
    public static readonly Guid Admin = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    public const string DriverEmail = "driver@saferoute.pe";
    public const string DriverPassword = "driver123";
    public static readonly Guid Driver = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    public const string ParentEmail = "parent@saferoute.pe";
    public const string ParentPassword = "parent123";
    public static readonly Guid Parent = Guid.Parse("b0000000-0000-0000-0000-000000000003");
}
