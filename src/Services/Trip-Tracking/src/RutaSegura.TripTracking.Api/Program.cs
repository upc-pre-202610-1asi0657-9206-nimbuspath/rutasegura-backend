// Etapa 1-2: el host solo expone health checks.
// En la Etapa 5 se registran AddTripTrackingApplication() + AddTripTrackingInfrastructure(),
// los endpoints REST, JWT y el hub de tiempo real.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live");

app.Run();

public partial class Program; // para WebApplicationFactory en las pruebas de integración
