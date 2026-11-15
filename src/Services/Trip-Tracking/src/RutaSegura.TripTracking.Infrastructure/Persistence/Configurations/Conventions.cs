using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Infrastructure.Persistence.Configurations;

/// <summary>
/// Conversores por convención: así los IDs fuertemente tipados y las fechas se configuran una sola vez
/// y el dominio no necesita atributos ni tipos de EF.
/// </summary>
internal static class Conventions
{
    public static void Apply(ModelConfigurationBuilder builder)
    {
        builder.Properties<TripId>().HaveConversion<TripIdConverter>();
        builder.Properties<RouteId>().HaveConversion<RouteIdConverter>();
        builder.Properties<StopId>().HaveConversion<StopIdConverter>();
        builder.Properties<StudentId>().HaveConversion<StudentIdConverter>();
        builder.Properties<DriverId>().HaveConversion<DriverIdConverter>();
        builder.Properties<VehicleId>().HaveConversion<VehicleIdConverter>();
        builder.Properties<IncidentId>().HaveConversion<IncidentIdConverter>();
        builder.Properties<OrganizationId>().HaveConversion<OrganizationIdConverter>();

        // MySQL no tiene tipo con zona horaria: se guarda el instante en UTC con microsegundos.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>().HaveColumnType("datetime(6)");
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>().HaveColumnType("datetime(6)");
    }

    /// <summary>tabla/columnas en snake_case (convención MySQL de 5.2.3), incluidos los complex types.</summary>
    public static void UseSnakeCaseColumns(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));

            foreach (var complex in entity.GetComplexProperties())
                RenameComplex(complex);

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName() ?? string.Empty));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
        }

        static void RenameComplex(Microsoft.EntityFrameworkCore.Metadata.IMutableComplexProperty complex)
        {
            foreach (var property in complex.ComplexType.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            foreach (var nested in complex.ComplexType.GetComplexProperties())
                RenameComplex(nested);
        }
    }

    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '.')
            {
                if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                continue;
            }

            if (char.IsUpper(c))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsUpper = i > 0 && char.IsUpper(name[i - 1]);
                if (sb.Length > 0 && sb[^1] != '_' && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

internal sealed class TripIdConverter() : ValueConverter<TripId, Guid>(id => id.Value, value => new TripId(value));
internal sealed class RouteIdConverter() : ValueConverter<RouteId, Guid>(id => id.Value, value => new RouteId(value));
internal sealed class StopIdConverter() : ValueConverter<StopId, Guid>(id => id.Value, value => new StopId(value));
internal sealed class StudentIdConverter() : ValueConverter<StudentId, Guid>(id => id.Value, value => new StudentId(value));
internal sealed class DriverIdConverter() : ValueConverter<DriverId, Guid>(id => id.Value, value => new DriverId(value));
internal sealed class VehicleIdConverter() : ValueConverter<VehicleId, Guid>(id => id.Value, value => new VehicleId(value));
internal sealed class IncidentIdConverter() : ValueConverter<IncidentId, Guid>(id => id.Value, value => new IncidentId(value));
internal sealed class OrganizationIdConverter() : ValueConverter<OrganizationId, Guid>(id => id.Value, value => new OrganizationId(value));

internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    value => value.UtcDateTime,
    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

/// <summary>
/// El conjunto de <see cref="ClientEventId"/> ya procesados se guarda como arreglo JSON en una columna del viaje:
/// es parte del agregado (idempotencia) y nunca se consulta por separado.
/// </summary>
internal static class ProcessedClientEventsMapping
{
    public static readonly ValueConverter<HashSet<ClientEventId>, string> Converter = new(
        set => JsonSerializer.Serialize(set.Select(e => e.Value).OrderBy(g => g), (JsonSerializerOptions?)null),
        json => new HashSet<ClientEventId>(
            (JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>()).Select(g => new ClientEventId(g))));

    public static readonly ValueComparer<HashSet<ClientEventId>> Comparer = new(
        (a, b) => a == null ? b == null : b != null && a.SetEquals(b),
        set => set.Aggregate(0, (hash, id) => hash ^ id.GetHashCode()),
        set => new HashSet<ClientEventId>(set));
}
