using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.Notification.Api.Security;
using RutaSegura.Notification.Api.Realtime;
using RutaSegura.Notification.Api.Errors;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Interfaces.Rest.Controllers;
using StackExchange.Redis;

// Composition root del microservicio; los contratos REST pertenecen a Interfaces.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
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

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

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
app.MapHealthChecks("/health/live");
app.Run();

public partial class Program;
