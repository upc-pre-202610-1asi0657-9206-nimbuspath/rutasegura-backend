namespace RutaSegura.ApiGateway.Proxy;

/// <summary>Sección <c>Gateway</c>: comportamiento del punto de entrada único.</summary>
public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>
    /// Leer la IP del cliente de <c>X-Forwarded-For</c>. Solo activarlo detrás de un balanceador/ingress de confianza:
    /// si no, cualquiera puede falsear su IP y saltarse el límite de intentos de inicio de sesión.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Tamaño máximo del cuerpo (por defecto 2 MB; un lote de 500 lecturas GPS pesa ~100 KB).</summary>
    public long MaxRequestBodyBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Intentos de inicio de sesión / registro por IP y ventana.</summary>
    public RateLimitOptions SignInRateLimit { get; set; } = new();

    /// <summary>Cluster → ruta de salud que se consulta en <c>/health/ready</c>.</summary>
    public Dictionary<string, string> Readiness { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(3);
}

public sealed class RateLimitOptions
{
    public int PermitLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Nombres compartidos entre la configuración (<c>appsettings.json</c>) y el código.</summary>
public static class GatewayNames
{
    public const string SignInRateLimitPolicy = "sign-in";
    public const string NotificationCluster = "notification";
    public const string DisabledMetadataKey = "RutaSegura.Disabled";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string ReadinessHttpClient = "gateway-readiness";
}
