using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using RutaSegura.ApiGateway.Errors;
using RutaSegura.ApiGateway.Health;
using RutaSegura.ApiGateway.Proxy;
using RutaSegura.ApiGateway.Security;

// API Gateway (YARP): punto de entrada único para la app del conductor, la del tutor y la web de la empresa.
//   /api/v1/users/**, /organizations/**            → Monolito (Iam)
//   /api/v1/roster/**, /routes/**, /operations/**  → Monolito (Administration)
//   /api/v1/stakeholders/**                         → Monolito (Stakeholder: hijos, foto, recojo, inscripciones)
//   /api/v1/trips/**, /tracking/**, /drivers/**, /guardians/**, /hubs/trip-tracking/** (WebSocket) → Trip & Tracking
//   /api/v1/notifications/**, … , /hubs/notifications/** → Notification (solo si NotificationProxy:Enabled)
// La autenticación y autorización las aplica cada servicio con la seguridad de Iam: el Gateway reenvía el token tal cual.
// Nunca se exponen /internal/**, /dev/**, swagger ni la creación de organizaciones (X-Provisioning-Key).

var builder = WebApplication.CreateBuilder(args);
var gateway = builder.Configuration.GetSection(GatewayOptions.Section).Get<GatewayOptions>() ?? new GatewayOptions();
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.Section));
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = gateway.MaxRequestBodyBytes);

var notificationProxyEnabled = builder.Configuration.GetValue<bool>("NotificationProxy:Enabled");
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
if (notificationProxyEnabled) builder.Services.AddNotificationProxySecurity(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddConfigFilter<ServiceAvailabilityConfigFilter>();

builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, _) => new ValueTask(GatewayProblems.WriteAsync(context.HttpContext,
        StatusCodes.Status429TooManyRequests, GatewayErrorCodes.TooManyRequests,
        "Demasiados intentos. Espera un momento antes de volver a intentar."));
    options.AddPolicy(GatewayNames.SignInRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = gateway.SignInRateLimit.PermitLimit,
            Window = gateway.SignInRateLimit.Window,
            QueueLimit = 0
        }));
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // Las apps móviles no usan CORS; esto es para la web de la empresa (Angular).
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
        .WithExposedHeaders(GatewayNames.CorrelationHeader, "Retry-After", "WWW-Authenticate");
}));

builder.Services.AddHttpClient(GatewayNames.ReadinessHttpClient);
builder.Services.AddHealthChecks().AddCheck<DownstreamReadinessCheck>("downstream", tags: ["ready"]);

if (gateway.TrustForwardedHeaders)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

var app = builder.Build();

if (gateway.TrustForwardedHeaders) app.UseForwardedHeaders();

// Id de correlación: viaja al servicio y vuelve en la respuesta (útil para reportar errores desde la app).
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue(GatewayNames.CorrelationHeader, out var value) || string.IsNullOrWhiteSpace(value) ||
        value.ToString().Length > 64)
        context.Request.Headers[GatewayNames.CorrelationHeader] = Guid.NewGuid().ToString("N");
    var correlationId = context.Request.Headers[GatewayNames.CorrelationHeader].ToString();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[GatewayNames.CorrelationHeader] = correlationId;
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Task.CompletedTask;
    });
    await next(context);
});

if (notificationProxyEnabled)
{
    var allowedOrigins = origins;
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/hubs/notifications") &&
            context.Request.Headers.TryGetValue("Origin", out var origin) &&
            !allowedOrigins.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase) &&
            !string.Equals(origin.ToString(), $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = (int)HttpStatusCode.Forbidden; return; }
        await next(context);
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// TLS termina en el ingress/servidor configurado para el despliegue.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapReverseProxy(proxy =>
{
    proxy.UseGatewayErrors();
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

app.MapFallback("{**path}", context => GatewayProblems.WriteAsync(context, StatusCodes.Status404NotFound, GatewayErrorCodes.RouteNotFound,
    "La ruta no existe en el API Gateway."));

app.Run();

public partial class Program;
