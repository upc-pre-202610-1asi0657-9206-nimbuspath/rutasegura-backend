using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddNotificationApplication();
builder.Services.AddNotificationInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Liveness verifica el proceso. Readiness requiere adaptadores reales en T05–T06.
app.MapHealthChecks("/health/live");
app.Run();

public partial class Program;
