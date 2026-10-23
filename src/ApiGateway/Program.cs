using RutaSegura.ApiGateway.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
var notificationProxyEnabled = builder.Configuration.GetValue<bool>("NotificationProxy:Enabled");
if (notificationProxyEnabled)
{
    builder.Services.AddNotificationProxySecurity(builder.Configuration);
    builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    {
        if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }));
}
var app = builder.Build();

if (notificationProxyEnabled)
{
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
    app.MapReverseProxy();
}
// TLS termina en el ingress/servidor configurado para el despliegue. Nunca exponer /internal/**.
app.MapHealthChecks("/health/live");

app.Run();

public partial class Program;
