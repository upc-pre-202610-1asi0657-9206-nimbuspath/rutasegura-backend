using System.ComponentModel.DataAnnotations;

namespace RutaSegura.TripTracking.Infrastructure.Options;

// Configuración externalizada (PT-11). Todas las secciones se validan al arrancar (ValidateOnStart).

public sealed class PersistenceOptions
{
    public const string ConnectionStringName = "TripTracking";
    public const string Section = "Persistence";

    /// <summary>Aplica las migraciones de EF Core al arrancar (docker-compose / desarrollo). En producción va en el pipeline.</summary>
    public bool MigrateOnStartup { get; set; }
}

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    /// <summary>false: sin broker (desarrollo). El Outbox acumula y el canal en vivo es en proceso.</summary>
    public bool Enabled { get; set; }

    [Required] public string HostName { get; set; } = "localhost";
    [Range(1, 65535)] public int Port { get; set; } = 5672;
    [Required] public string UserName { get; set; } = "guest";
    [Required] public string Password { get; set; } = "guest";
    [Required] public string VirtualHost { get; set; } = "/";
    public string ClientName { get; set; } = "trip-tracking";

    /// <summary>Exchange topic donde se publican los eventos de integración (Outbox).</summary>
    [Required] public string EventsExchange { get; set; } = "trip-tracking.events";

    /// <summary>Exchange topic de telemetría en vivo (mensajes no persistentes).</summary>
    [Required] public string LiveExchange { get; set; } = "trip-tracking.live";

    /// <summary>Cola durable con los eventos que este servicio consume de otros contextos.</summary>
    [Required] public string InboundQueue { get; set; } = "trip-tracking.inbound";

    [Range(1, 1000)] public ushort PrefetchCount { get; set; } = 20;

    /// <summary>
    /// Suscripciones de la cola de entrada: exchange de origen + routing key (admite comodines topic).
    /// Vacío por defecto: se definen en appsettings para no duplicarse al enlazar configuración.
    /// </summary>
    public List<RabbitMqBinding> InboundBindings { get; set; } = [];
}

public sealed class RabbitMqBinding
{
    [Required] public string Exchange { get; set; } = string.Empty;
    [Required] public string RoutingKey { get; set; } = "#";
}

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
    [Range(1, 500)] public int BatchSize { get; set; } = 50;

    /// <summary>Espera máxima entre reintentos de un mensaje (backoff exponencial 2^n s hasta este tope).</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Mensajes publicados se borran después de este tiempo (limpieza).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}

public sealed class LivePositionOptions
{
    public const string Section = "LivePositions";

    /// <summary>TTL de <c>trip:{id}:position</c> en Redis. Cubre cortes de señal razonables del conductor.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class SchedulingOptions
{
    public const string Section = "Scheduling";

    /// <summary>Programa automáticamente los viajes del día a partir de la réplica de rutas.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Días hacia adelante (0 = solo hoy; 1 = hoy y mañana).</summary>
    [Range(0, 7)] public int DaysAhead { get; set; } = 1;

    /// <summary>No se crean viajes cuya hora de salida ya pasó hace más de esto.</summary>
    public TimeSpan SkipIfStartedMoreThan { get; set; } = TimeSpan.FromHours(1);
}

public sealed class DemoSeedOptions
{
    public const string Section = "Seed";

    /// <summary>Carga la "Ruta 3" del prototipo, un tutor vinculado y el viaje de hoy (solo desarrollo / demo).</summary>
    public bool Demo { get; set; }
}
