extern alias notificationApi;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Tests.Fakes;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Tests.Host;

internal sealed class NotificationApiFactory : WebApplicationFactory<notificationApi::Program>
{
    public const string Secret = "notification-api-tests-secret-0123456789-abcdefghij";
    public readonly Guid Organization = Guid.NewGuid(), Guardian = Guid.NewGuid(), Student = Guid.NewGuid();
    public readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddSeconds(-2);
    public readonly MemoryStore Store = new();
    public readonly DirectoryFake Directory = new();
    public readonly UnitOfWorkFake UnitOfWork = new();

    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public NotificationApiFactory(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _settings = settings ?? new();
        _configureServices = configureServices;
        Directory.Students.Add(new(Organization, Guardian, Student));
        Directory.Operators.Add(new(Organization, Guardian));
    }

    public NotificationEntity SeedNotice()
    {
        var notice = NotificationEntity.Create(Organization, Guardian, Guid.NewGuid(), "test.event.v1", "api-test",
            NotificationCategory.Boarding, NotificationContent.Create("Abordaje", "Contenido autorizado"), Now, Now, studentId: Student);
        Store.Add(notice);
        return notice;
    }

    public string Token(Guid? user = null, Guid? organization = null, string role = "PARENT", bool expired = false, bool includeTenant = true, string? secret = null)
    {
        var claims = new List<Claim> { new("sub", (user ?? Guardian).ToString()), new("role", role) };
        if (includeTenant) claims.Add(new("organizationId", (organization ?? Organization).ToString()));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expired ? -2 : 5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? Secret)), SecurityAlgorithms.HmacSha256)));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(Root(), "src/Services/Notification/src/RutaSegura.Notification.Api"));
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationRepository>(); services.AddSingleton<INotificationRepository>(Store);
            services.RemoveAll<IAnnouncementRepository>(); services.AddSingleton<IAnnouncementRepository>(Store);
            services.RemoveAll<IProcessedNotificationEventRepository>(); services.AddSingleton<IProcessedNotificationEventRepository>(Store);
            services.RemoveAll<IPushSubscriptionRepository>(); services.AddSingleton<IPushSubscriptionRepository>(Store);
            services.RemoveAll<INotificationDispatchQueue>(); services.AddSingleton<INotificationDispatchQueue>(Store);
            services.RemoveAll<IUnitOfWork>(); services.AddSingleton<IUnitOfWork>(UnitOfWork);
            services.RemoveAll<INotificationRecipientDirectory>(); services.AddSingleton<INotificationRecipientDirectory>(Directory);
            services.RemoveAll<ITripNotificationContext>(); services.AddSingleton<ITripNotificationContext>(Directory);
            _configureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["NotificationApi:Enabled"] = "true", ["NotificationRabbitMq:Enabled"] = "false", ["NotificationChannels:Enabled"] = "false",
            ["Iam:AccountValidation:Enabled"] = "false", // IAM se sustituye explicitamente en los tests aislados.
            ["ConnectionStrings:NotificationDatabase"] = "Server=127.0.0.1;Port=1;Database=notification_api_test;UserID=unused",
            ["NotificationPersistence:PushTokenEncryptionKey"] = Convert.ToBase64String(new byte[32]), ["Jwt:Secret"] = Secret,
            ["Cors:AllowedOrigins:0"] = "https://web.test"
        };
        foreach (var name in new[] { "Administration", "TripTracking" })
        {
            settings[$"NotificationDependencies:{name}:BaseUrl"] = "https://service.internal/";
            settings[$"NotificationDependencies:{name}:ServiceToken"] = new string('s', 32);
        }
        foreach (var setting in _settings) settings[setting.Key] = setting.Value;
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(settings));
        return base.CreateHost(builder);
    }

    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RutaSegura.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
