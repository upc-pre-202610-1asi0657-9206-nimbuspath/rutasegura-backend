using RutaSegura.IAM.Application.DependencyInjection;
using RutaSegura.IAM.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddIAMApplication();
builder.Services.AddIAMInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Liveness verifica el proceso. Readiness requiere adaptadores reales en T05–T06.
app.MapHealthChecks("/health/live");
app.Run();

public partial class Program;
