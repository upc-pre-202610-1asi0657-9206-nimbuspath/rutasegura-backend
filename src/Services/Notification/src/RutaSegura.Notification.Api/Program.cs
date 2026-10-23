using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Infrastructure.DependencyInjection;

// Composition root del microservicio; los contratos REST pertenecen a Interfaces.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
// Fase 1: el core se verifica en pruebas; los casos de uso requieren los adaptadores productivos de las fases 2-4.
builder.Services.AddNotificationApplication(registerUseCases: false);
builder.Services.AddNotificationInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Liveness verifica el proceso. Readiness requiere adaptadores reales en T05–T06.
app.MapHealthChecks("/health/live");
app.Run();

public partial class Program;
