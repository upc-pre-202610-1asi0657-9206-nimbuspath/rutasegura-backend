var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
var app = builder.Build();

// Enrutamiento YARP, JWT y HTTPS se configuran en T11–T14.
app.MapHealthChecks("/health/live");

app.Run();

public partial class Program;
