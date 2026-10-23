using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Infrastructure.DependencyInjection;

// Composition root del microservicio; los contratos REST pertenecen a Interfaces.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
// El worker activa casos de uso con puertos HTTP reales. No habilita REST ni canales FCM/SignalR todavia.
builder.Services.AddNotificationApplication(registerUseCases: builder.Configuration.GetValue<bool>("NotificationRabbitMq:Enabled"));
builder.Services.AddNotificationInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Liveness verifica el proceso. Readiness integral se incorpora con el despliegue de las dependencias.
app.MapHealthChecks("/health/live");
app.Run();

public partial class Program;
