using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Monolith.Iam.Application.Internal.OutboundServices;
using Monolith.Iam.Infrastructure.Persistence.EFC.Seeding;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace RutaSegura.Monolith.Administration.IntegrationTests.Support;

/// <summary>
///     The real monolith host (Program.cs: controllers, Iam security, Administration, EF Core) on an in-memory SQLite
///     database built from the same EF model and seeded with the Iam demo accounts. One instance per test.
/// </summary>
public sealed class MonolithApp : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Secrets of the test environment (the real ones are never in appsettings.json).</summary>
    public const string TestTokenSecret = "integration-tests-secret-0123456789-abcdefghijklmnop";
    public const string TestProvisioningKey = "integration-tests-provisioning-key-0123456789";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Dictionary<string, string?> _settings;

    public MonolithApp(int maxActiveRoutes = 2, IDictionary<string, string?>? settings = null)
    {
        _connection.Open();
        _settings = new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
            ["ConnectionStrings:DefaultConnection"] = "Server=unused-in-tests",
            ["TokenSettings:Secret"] = TestTokenSecret,
            ["Iam:Provisioning:ApiKey"] = TestProvisioningKey,
            ["Administration:Plans:Default:PlanCode"] = "BASIC",
            ["Administration:Plans:Default:MaxActiveRoutes"] = maxActiveRoutes.ToString(),
            ["Administration:Messaging:Enabled"] = "false"
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) _settings[key] = value;
    }

    public string TokenSecret => Setting("TokenSettings:Secret")!;
    public string? TokenIssuer => Setting("TokenSettings:Issuer");
    public string? TokenAudience => Setting("TokenSettings:Audience");

    private string? Setting(string key) =>
        Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()[key];

    /// <summary>Builds a token like Iam's TokenService (same secret, issuer and audience as the app).</summary>
    public string Token(Guid userId, string role, Guid? organizationId, DateTime? notBefore = null,
        DateTime? expires = null, string? audience = null) =>
        TestTokens.Create(TokenSecret, userId, role, organizationId, notBefore, expires, TokenIssuer,
            audience ?? TokenAudience);

    /// <summary>Provisions an organization with its administrator through the platform endpoint and signs it in.</summary>
    public async Task<(Guid OrganizationId, Guid AdministratorId, HttpClient Administrator)> ProvisionOrganizationAsync(
        string name, string adminEmail, string adminPassword = "Password123")
    {
        var platform = CreateClient();
        platform.DefaultRequestHeaders.Add("X-Provisioning-Key", TestProvisioningKey);
        var response = await platform.PostAsJsonAsync("/api/v1/organizations", new
        {
            name,
            administrator = new { firstName = "Admin", lastName = name, email = adminEmail, password = adminPassword }
        }, Json, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return (body.GetProperty("organization").GetProperty("id").GetGuid(),
            body.GetProperty("administrator").GetProperty("id").GetGuid(),
            await SignInAsync(adminEmail, adminPassword));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings) builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            // Swap the MySQL provider configured in Program.cs for SQLite.
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
        IamSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<IHashingService>())
            .GetAwaiter().GetResult();
        return host;
    }

    /// <summary>Signs in through Iam (<c>POST /api/v1/users/sign-in</c>) and returns a client with the JWT.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/users/sign-in", new { email, password }, Json,
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return WithToken(body.GetProperty("token").GetString()!);
    }

    public HttpClient WithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<HttpClient> AdminAsync() => SignInAsync(SeedAccounts.AdminEmail, SeedAccounts.AdminPassword);

    /// <summary>Runs code against the database with a fresh scope (what a new request would see).</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

/// <summary>Accounts created by <c>IamSeeder</c> in the organization "Transportes Escolares Lima Norte".</summary>
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

    public static readonly Guid SecondDriver = Guid.Parse("b0000000-0000-0000-0000-000000000004");
}
