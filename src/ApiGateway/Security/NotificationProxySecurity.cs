using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace RutaSegura.ApiGateway.Security;

public static class NotificationProxySecurity
{
    public static IServiceCollection AddNotificationProxySecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? "";
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Configura Jwt:Secret compatible con IAM para habilitar el proxy.");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
                ValidateIssuer = !string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]), ValidIssuer = configuration["Jwt:Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]), ValidAudience = configuration["Jwt:Audience"]
            };
            options.Events = new() { OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs/notifications") && context.Request.Query.TryGetValue("access_token", out var token)) context.Token = token;
                return Task.CompletedTask;
            } };
        });
        services.AddAuthorizationBuilder().AddPolicy("notification-proxy", policy => policy.RequireAuthenticatedUser());
        return services;
    }
}
