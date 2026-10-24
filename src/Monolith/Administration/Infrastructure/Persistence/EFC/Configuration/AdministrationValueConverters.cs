using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Administration.Infrastructure.Persistence.EFC.Configuration;

/// <summary>Value converters shared by the Administration mapping (the domain has no EF attributes or types).</summary>
public static class AdministrationValueConverters
{
    public static readonly ValueConverter<OrganizationId, Guid> OrganizationIdConverter =
        new(id => id.Identifier, value => new OrganizationId(value));

    public static readonly ValueConverter<UserId, Guid> UserIdConverter = new(id => id.Identifier, value => new UserId(value));

    public static readonly ValueConverter<DriverId, Guid> DriverIdConverter = new(id => id.Identifier, value => new DriverId(value));

    public static readonly ValueConverter<VehicleId, Guid> VehicleIdConverter =
        new(id => id.Identifier, value => new VehicleId(value));

    public static readonly ValueConverter<RouteId, Guid> RouteIdConverter = new(id => id.Identifier, value => new RouteId(value));

    public static readonly ValueConverter<RouteStopId, Guid> RouteStopIdConverter =
        new(id => id.Identifier, value => new RouteStopId(value));

    public static readonly ValueConverter<OperationalAlertId, Guid> OperationalAlertIdConverter =
        new(id => id.Identifier, value => new OperationalAlertId(value));

    public static readonly ValueConverter<TripRecordId, Guid> TripRecordIdConverter =
        new(id => id.Identifier, value => new TripRecordId(value));

    public static readonly ValueConverter<TripId, Guid> TripIdConverter = new(id => id.Identifier, value => new TripId(value));

    /// <summary>MySQL has no time-zone aware type: the instant is stored in UTC with microseconds.</summary>
    public static readonly ValueConverter<DateTimeOffset, DateTime> UtcDateTimeConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    public static readonly ValueConverter<DateOnly, DateTime> DateOnlyConverter = new(
        value => value.ToDateTime(TimeOnly.MinValue),
        value => DateOnly.FromDateTime(value));

    public static readonly ValueConverter<TimeOnly, TimeSpan> TimeOnlyConverter = new(
        value => value.ToTimeSpan(),
        value => TimeOnly.FromTimeSpan(value));

    /// <summary>Students of a stop as a JSON array of ids: part of the aggregate, never queried on their own.</summary>
    public static readonly ValueConverter<List<StudentId>, string> StudentIdsConverter = new(
        students => JsonSerializer.Serialize(students.Select(s => s.Identifier).ToList(), (JsonSerializerOptions?)null),
        json => (JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>())
            .Select(id => new StudentId(id)).ToList());

    public static readonly ValueComparer<List<StudentId>> StudentIdsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        students => students.Aggregate(0, (hash, student) => HashCode.Combine(hash, student.Identifier)),
        students => students.ToList());
}
