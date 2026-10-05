using RutaSegura.TripTracking.Infrastructure.DependencyInjection;

// Solo se expone liveness. Los handlers existentes requieren puertos de persistencia,
// autorización y mensajería todavía pendientes; no se registran adaptadores ficticios.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddTripTrackingInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live");

app.Run();

public partial class Program; // para WebApplicationFactory en las pruebas de integración
