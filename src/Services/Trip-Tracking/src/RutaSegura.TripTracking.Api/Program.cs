using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using RutaSegura.TripTracking.Api.Controllers;
using RutaSegura.TripTracking.Api.Development;
using RutaSegura.TripTracking.Api.Errors;
using RutaSegura.TripTracking.Api.Realtime;
using RutaSegura.TripTracking.Api.Security;
using RutaSegura.TripTracking.Application.DependencyInjection;
using RutaSegura.TripTracking.Infrastructure.DependencyInjection;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.Infrastructure.Options;
using RutaSegura.TripTracking.Infrastructure.Persistence;

// Composition root del Trip & Tracking Service (hexágono):
//   entrada  → REST /api/v1 (controllers) + SignalR /hubs/trip-tracking
//   núcleo   → Application (IDispatcher: Logging → Validation → Transaction) + Domain
//   salida   → Infrastructure (MySQL/EF Core + Outbox, RabbitMQ, Redis, workers)

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TripTrackingExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecurityTransformer>());
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Seguridad que provee Iam: JWT de Iam (mismo secreto, emisor y audiencia), políticas por rol/organización y
// verificación en Iam de que la cuenta sigue vigente (GET /api/v1/users/me, con caché).
builder.Services.AddTripTrackingSecurity(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // ~1 lote cada 3-5 s por conductor en línea; holgura para ráfagas al recuperar señal.
    options.AddPolicy(TrackingController.TelemetryRateLimit, http => RateLimitPartition.GetSlidingWindowLimiter(
        http.User.UserId().ToString(),
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }));
});

builder.Services.AddSingleton<ILiveUpdateSink, SignalRLiveUpdateSink>();
builder.Services.AddTripTrackingApplication();
builder.Services.AddTripTrackingInfrastructure(builder.Configuration);

var app = builder.Build();

if (args.Contains("--migrate") || app.Configuration.GetValue<bool>($"{PersistenceOptions.Section}:{nameof(PersistenceOptions.MigrateOnStartup)}"))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
    if (args.Contains("--migrate-only")) return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Trip & Tracking v1");
        o.RoutePrefix = "swagger";
    });
    if (app.Configuration.GetValue<bool>("DevTokens:Enabled"))
    {
        app.MapDevTokens();
        // Los tokens de /dev/tokens son de usuarios demo que Iam no conoce: Iam los rechazaría.
        if (app.Configuration.GetValue($"{IamOptions.Section}:AccountValidation:Enabled", true))
            app.Logger.LogWarning("DevTokens:Enabled=true con Iam:AccountValidation:Enabled=true: Iam rechazará " +
                                  "los tokens de /dev/tokens. Usa el perfil http-demo-sin-iam o inicia sesión en Iam.");
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<TripTrackingHub>(SecurityExtensions.HubPath);

// Liveness: el proceso responde. Readiness: MySQL + RabbitMQ (+ Redis como Degraded).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(TripTrackingInfrastructureExtensions.ReadinessTag)
});

app.Run();

public partial class Program; // para WebApplicationFactory en las pruebas de integración
