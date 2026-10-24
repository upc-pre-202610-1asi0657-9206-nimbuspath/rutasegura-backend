using Monolith.Administration.Domain.Model.ValueObjects;

namespace Monolith.Administration.Domain.Model.Entities;

/// <summary>
///     A stop of a route: the place where its students are picked up (PICKUP route) or dropped off (RETURN
///     route). Only the <see cref="Aggregates.Route" /> aggregate creates or changes its stops.
/// </summary>
public class RouteStop
{
    public const int NameMaxLength = 100;
    public const int AddressMaxLength = 200;

    private readonly List<StudentId> _students = [];

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected RouteStop()
    {
        Id = new RouteStopId();
        Name = string.Empty;
        Location = new GeoLocation();
    }

    internal RouteStop(int sequence, string name, string? address, GeoLocation location)
    {
        Id = RouteStopId.New();
        Sequence = sequence;
        Name = ValidName(name);
        Address = ValidAddress(address);
        Location = location ?? throw new ArgumentNullException(nameof(location));
    }

    public RouteStopId Id { get; private set; }

    /// <summary>1-based position of the stop in the route.</summary>
    public int Sequence { get; private set; }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public GeoLocation Location { get; private set; }

    /// <summary>Students picked up / dropped off at this stop.</summary>
    public IReadOnlyCollection<StudentId> Students => _students.AsReadOnly();

    public bool HasStudent(StudentId studentId) => _students.Contains(studentId);

    internal void Update(string name, string? address, GeoLocation location)
    {
        Name = ValidName(name);
        Address = ValidAddress(address);
        Location = location ?? throw new ArgumentNullException(nameof(location));
    }

    internal void ChangeSequence(int sequence) => Sequence = sequence;

    internal void AddStudent(StudentId studentId)
    {
        if (!_students.Contains(studentId))
            _students.Add(studentId);
    }

    internal void RemoveStudent(StudentId studentId) => _students.Remove(studentId);

    private static string ValidName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new ArgumentException($"Stop name is required (max {NameMaxLength} characters).", nameof(name));
        return trimmed;
    }

    private static string? ValidAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var trimmed = address.Trim();
        if (trimmed.Length > AddressMaxLength)
            throw new ArgumentException($"Stop address is too long (max {AddressMaxLength} characters).",
                nameof(address));
        return trimmed;
    }
}
