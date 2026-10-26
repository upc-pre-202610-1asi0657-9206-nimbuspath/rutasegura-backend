using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Api.Security;
using RutaSegura.Notification.Api.Realtime;
using RutaSegura.Notification.Api.Errors;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Interfaces.Rest.Controllers;
using StackExchange.Redis;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using RutaSegura.Notification.Api.Health;
using RutaSegura.Notification.Api.Operations;
using RutaSegura.Notification.Api.OpenApi;
using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using OpenTelemetry.Metrics;

// Composition root del microservicio; los contratos REST pertenecen a Interfaces.
var migrateOnly = args.Contains("--migrate", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--migrate").ToArray());
// Docker secrets usa nombres de archivo jerarquicos (por ejemplo Jwt__Secret). El entorno sigue teniendo precedencia.
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true).AddEnvironmentVariables();
if (migrateOnly)
{
    try
    {
        builder.Services.AddNotificationPersistence(builder.Configuration.GetConnectionString("NotificationDatabase") ?? "",
            builder.Configuration["NotificationPersistence:PushTokenEncryptionKey"] ?? "");
        await using var migrationHost = builder.Build();
        await using var scope = migrationHost.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
        Console.WriteLine("Migraciones de Notification aplicadas.");
    }
    catch (Exception) { Console.Error.WriteLine("No se pudieron aplicar las migraciones de Notification. Revisa configuracion y disponibilidad de MySQL."); Environment.ExitCode = 1; }
    return;
}

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<NotificationOpenApi>());
builder.Services.AddNotificationMonitoring(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<NotificationReadinessCheck>("notification-ready", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));
var apiEnabled = builder.Configuration.GetValue<bool>("NotificationApi:Enabled");
var channelsEnabled = builder.Configuration.GetValue<bool>("NotificationChannels:Enabled");
var messagingEnabled = builder.Configuration.GetValue<bool>("NotificationRabbitMq:Enabled");
if (apiEnabled)
{
    builder.Services.AddNotificationSecurity(builder.Configuration);
    builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly)
        .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new ProblemDetails
        { Status = 400, Title = "Solicitud invalida.", Extensions = { ["code"] = "NOTIFICATION_INVALID_REQUEST" } }));
    builder.Services.AddExceptionHandler<NotificationExceptionHandler>();
    builder.Services.AddProblemDetails();
    var signalR = builder.Services.AddSignalR(options => { options.EnableDetailedErrors = false; options.MaximumReceiveMessageSize = 8192; });
    var redis = builder.Configuration["NotificationRealtime:RedisConnection"];
    if (!string.IsNullOrWhiteSpace(redis)) signalR.AddStackExchangeRedis(redis, options => options.Configuration.ChannelPrefix = RedisChannel.Literal("rutasegura.notification"));
    if (!string.IsNullOrWhiteSpace(redis))
    {
        builder.Services.AddSingleton<NotificationRedisReadinessCheck>();
        builder.Services.AddHealthChecks().AddCheck<NotificationRedisReadinessCheck>("notification-redis", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));
    }
    builder.Services.AddSingleton<IRealtimeNotificationSender, SignalRNotificationSender>();
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    {
        if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }));
}
builder.Services.AddNotificationApplication(registerUseCases: apiEnabled || messagingEnabled || channelsEnabled);
builder.Services.AddNotificationInfrastructure(builder.Configuration);

var app = builder.Build();

// Estos endpoints operativos nunca se publican en Gateway ni aceptan JWT de familias como credencial de monitoreo.
app.Use(async (context, next) =>
{
    if ((context.Request.Path.StartsWithSegments("/metrics") || context.Request.Path.StartsWithSegments("/openapi")) &&
        !NotificationOperations.Authorized(context, app.Configuration))
    { context.Response.StatusCode = 404; return; }
    await next(context);
});
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("NotificationOpenApi:Enabled"))
    app.MapOpenApi();
if (app.Configuration.GetValue<bool>("NotificationMonitoring:Enabled")) app.MapPrometheusScrapingEndpoint();

if (apiEnabled)
{
    app.UseExceptionHandler();
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/hubs/notifications") &&
            context.Request.Headers.TryGetValue("Origin", out var origin) &&
            !allowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase) &&
            !string.Equals(origin.ToString(), $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = 403; return; }
        await next(context);
    });
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHub<NotificationHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true);
}
// No hay endpoints internos ni metodos de publicacion de eventos expuestos al navegador.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

public partial class Program;
