using System.Text;
using Cortex.Mediator.Commands;
using Cortex.Mediator.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Monolith.Iam.Application.Internal.CommandServices;
using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Application.Internal.OutboundServices;
using Monolith.Iam.Application.Internal.QueryServices;
using Monolith.Iam.Application.QueryServices;
using Monolith.Iam.Domain.Repositories;
using Monolith.Iam.Infrastructure.Hashing.BCrypt.Services;
using Monolith.Iam.Infrastructure.Persistence.EFC.Repositories;
using Monolith.Iam.Infrastructure.Persistence.EFC.Seeding;
using Monolith.Iam.Infrastructure.Tokens.Jwt.Configuration;
using Monolith.Iam.Infrastructure.Tokens.Jwt.Services;
using Monolith.Resources.Errors;
using Monolith.Resources.Shared;
using Monolith.Shared.Domain.Repositories;
using Monolith.Shared.Infrastructure.Interfaces.AspNetCore.Configuration;
using Monolith.Shared.Infrastructure.Mediator.Cortex.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Infrastructure.Persistence.EFC.Repositories;
using Monolith.Shared.Infrastructure.Pipeline.Middleware.Extensions;
using Monolith.Shared.Interfaces.Rest.ProblemDetails;

var builder = WebApplication.CreateBuilder(args);
// ---------------------------------------------------------------------------
// Routing & Controllers
// ---------------------------------------------------------------------------
builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddControllers(options => options.Conventions.Add(new KebabCaseRouteNamingConvention()))
    .AddDataAnnotationsLocalization();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
// ---------------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllPolicy",
        policy =>
        {
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>()?
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .ToArray();

            if (allowedOrigins is { Length: > 0 })
                policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
            else
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        });
});
// ---------------------------------------------------------------------------
// Database (MySQL via EF Core)
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var connectionStringTemplate = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionStringTemplate))
        throw new InvalidOperationException("Database connection string is not set in the configuration.");

    var connectionString = Environment.ExpandEnvironmentVariables(connectionStringTemplate);

    options.UseMySQL(connectionString)
        .UseLoggerFactory(serviceProvider.GetRequiredService<ILoggerFactory>())
        .EnableDetailedErrors();

    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// ---------------------------------------------------------------------------
// Authentication (JWT Bearer) — issued by the Iam bounded context
// ---------------------------------------------------------------------------
builder.Services.Configure<TokenSettings>(builder.Configuration.GetSection("TokenSettings"));

var tokenSecret = builder.Configuration["TokenSettings:Secret"];
if (string.IsNullOrWhiteSpace(tokenSecret))
    throw new InvalidOperationException("TokenSettings:Secret is not set in the configuration.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Localization
// ---------------------------------------------------------------------------
builder.Services.AddLocalization();
builder.Services.AddSingleton<IStringLocalizer<ErrorMessages>, StringLocalizer<ErrorMessages>>();
builder.Services.AddSingleton<IStringLocalizer<CommonMessages>, StringLocalizer<CommonMessages>>();

// Custom RFC 7807 problem details factory.
builder.Services.AddSingleton<ProblemDetailsFactory>();

// ---------------------------------------------------------------------------
// Swagger / OpenAPI
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "Monolith RutaSegura API",
            Version = "v1",
            Description = "Monolith API",
            License = new OpenApiLicense
            {
                Name = "Apache 2.0",
                Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0.html")
            }
        });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
    options.EnableAnnotations();
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
});


// ---------------------------------------------------------------------------
// Dependency Injection per Bounded Context
// ---------------------------------------------------------------------------

// Shared
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Iam bounded context
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IIamCommandService, IamCommandService>();
builder.Services.AddScoped<IIamQueryService, IamQueryService>();
builder.Services.AddScoped<IHashingService, HashingService>();
builder.Services.AddScoped<ITokenService, TokenService>();


// ---------------------------------------------------------------------------
// Mediator (Cortex) — command pipeline behaviors and event handling
// ---------------------------------------------------------------------------
builder.Services.AddScoped(typeof(ICommandPipelineBehavior<>), typeof(LoggingCommandBehavior<>));
builder.Services.AddCortexMediator([typeof(Program)]);

var app = builder.Build();

// Apply pending migrations on startup (safe to call even when the schema is up to date).
// Wrapped so a database connectivity/credential problem does not prevent the API (and Swagger)
// from starting: the app boots, surfaces a clear log entry, and database-backed endpoints fail
// individually until the connection is fixed.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var startupLogger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
        startupLogger.LogInformation("Database migrations applied successfully.");

        // Seed demo data on an empty database, in dependency order across bounded contexts:
        // identity/org first, then stakeholders, then plans/subscription, routes, trips and
        // finally notifications that reference the seeded parent and trip.
        var hashingService = services.GetRequiredService<IHashingService>();
        await IamSeeder.SeedAsync(context, hashingService);
        startupLogger.LogInformation("Seed data ensured.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex,
            "Database migration failed at startup. The API will still start, but database-backed " +
            "endpoints will fail until the connection string in appsettings is corrected.");
    }
}

// ---------------------------------------------------------------------------
// HTTP request pipeline
// ---------------------------------------------------------------------------
app.UseGlobalExceptionHandler();

var supportedCultures = new[] { "en", "es" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

app.UseForwardedHeaders();

var swaggerEnabled = app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false);
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAllPolicy");
// Azure App Service terminates TLS before forwarding traffic to the container.
// Keep this opt-in to avoid redirect loops behind that reverse proxy.
if (app.Configuration.GetValue("HttpsRedirection:Enabled", false))
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();