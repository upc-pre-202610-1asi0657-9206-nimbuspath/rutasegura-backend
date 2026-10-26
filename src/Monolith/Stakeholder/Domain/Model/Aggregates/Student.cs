using Monolith.Shared.Domain.Model.Events;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Model.Entities;
using Monolith.Stakeholder.Domain.Model.Events;
using Monolith.Stakeholder.Domain.Model.Exceptions;
using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Model.Aggregates;

/// <summary>
///     A child registered by its parent (guardian): name, school data, photo, the address where it is picked up and
///     the routes it is enrolled in.
/// </summary>
/// <remarks>
///     Invariants guarded here:
///     <list type="bullet">
///         <item>Only the guardian who registered the child manages it.</item>
///         <item>A child is enrolled at most once in the same route and in one route per direction (PICKUP / RETURN).</item>
///         <item>A removed child cannot change or be enrolled, and cannot be removed while it still rides a route.</item>
///     </list>
///     Placing the child on a stop is Route Planning's job; the application layer asks it through a facade inside the
///     same transaction.
/// </remarks>
public partial class Student : IHasDomainEvents
{
    public const int GradeMaxLength = 40;
    public const int SchoolMaxLength = 100;
    public const int MaxStudentsPerGuardian = 10;
    public const int MinimumAgeYears = 2;
    public const int MaximumAgeYears = 20;

    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<RouteEnrollment> _enrollments = [];

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Student()
    {
        Id = new StudentId();
        GuardianUserId = Guid.Empty;
        FullName = new FullName();
        PickupLocation = new PickupLocation();
        Status = Active;
    }

    public Student(Guid guardianUserId, FullName fullName, DateOnly? birthDate, string? schoolGrade,
        string? schoolName, PickupLocation pickupLocation, DateOnly today, DateTimeOffset now)
    {
        if (guardianUserId == Guid.Empty)
            throw new ArgumentException("The guardian of the student is required.", nameof(guardianUserId));
        Id = StudentId.New();
        GuardianUserId = guardianUserId;
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        BirthDate = ValidBirthDate(birthDate, today);
        SchoolGrade = Optional(schoolGrade, GradeMaxLength, "school grade");
        SchoolName = Optional(schoolName, SchoolMaxLength, "school name");
        PickupLocation = pickupLocation ?? throw new ArgumentNullException(nameof(pickupLocation));
        Status = Active;
        _domainEvents.Add(new StudentRegisteredEvent(Id, GuardianUserId, now));
    }

    public const string Active = "ACTIVE";
    public const string Removed = "REMOVED";

    public StudentId Id { get; private set; }

    /// <summary>Iam user id of the parent (role PARENT) who registered the child.</summary>
    public Guid GuardianUserId { get; private set; }

    public FullName FullName { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    /// <summary>E.g. "3.° de primaria".</summary>
    public string? SchoolGrade { get; private set; }

    public string? SchoolName { get; private set; }

    public PickupLocation PickupLocation { get; private set; }

    public StudentPhoto? Photo { get; private set; }

    /// <summary>ACTIVE or REMOVED (kept for the trip history of the transport company).</summary>
    public string Status { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public IReadOnlyList<RouteEnrollment> Enrollments => _enrollments.OrderBy(e => e.EnrolledAt).ToList();

    public IReadOnlyList<RouteEnrollment> ActiveEnrollments => _enrollments.Where(e => e.IsActive()).ToList();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool IsRemoved() => Status == Removed;

    public bool IsGuardedBy(Guid userId) => userId != Guid.Empty && GuardianUserId == userId;

    public RouteEnrollment? FindEnrollment(EnrollmentId enrollmentId) =>
        _enrollments.FirstOrDefault(e => e.Id == enrollmentId);

    public bool IsEnrolledIn(Guid routeId) => _enrollments.Any(e => e.IsActive() && e.RouteId == routeId);

    // ── Profile ──────────────────────────────────────────────────────────────

    public void UpdateDetails(FullName fullName, DateOnly? birthDate, string? schoolGrade, string? schoolName,
        DateOnly today)
    {
        EnsureNotRemoved();
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        BirthDate = ValidBirthDate(birthDate, today);
        SchoolGrade = Optional(schoolGrade, GradeMaxLength, "school grade");
        SchoolName = Optional(schoolName, SchoolMaxLength, "school name");
    }

    /// <summary>
    ///     Moves the pickup point. Returns the active enrollments whose route must re-place the child (Route Planning
    ///     moves it to the stop of the new address).
    /// </summary>
    public IReadOnlyList<RouteEnrollment> ChangePickupLocation(PickupLocation location, DateTimeOffset now)
    {
        EnsureNotRemoved();
        ArgumentNullException.ThrowIfNull(location);
        if (location == PickupLocation) return [];
        PickupLocation = location;
        _domainEvents.Add(new StudentPickupLocationChangedEvent(Id, now));
        return ActiveEnrollments;
    }

    public void ReplacePhoto(StudentPhoto photo)
    {
        EnsureNotRemoved();
        Photo = photo ?? throw new ArgumentNullException(nameof(photo));
    }

    public void RemovePhoto()
    {
        if (Photo is null)
            throw new StakeholderDomainException(StakeholderError.StudentPhotoNotFound, "The student has no photo.");
        Photo = null;
    }

    // ── Enrollments ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Enrolls the child in a route of a transport company (the route accepted enrollments; checked beforehand
    ///     through Route Planning).
    /// </summary>
    public RouteEnrollment Enroll(Guid routeId, Guid organizationId, string routeName, string direction,
        DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (IsEnrolledIn(routeId))
            throw new StakeholderDomainException(StakeholderError.StudentAlreadyEnrolled,
                $"{FullName} is already enrolled in this route.");

        var enrollment = new RouteEnrollment(routeId, organizationId, routeName, direction, now);
        var sameDirection = _enrollments.FirstOrDefault(e => e.IsActive() && e.Direction == enrollment.Direction);
        if (sameDirection is not null)
            throw new StakeholderDomainException(StakeholderError.EnrollmentDirectionTaken,
                $"{FullName} already has a {DirectionLabel(enrollment.Direction)} route ({sameDirection.RouteName}); " +
                "withdraw it first.");

        _enrollments.Add(enrollment);
        _domainEvents.Add(new StudentEnrolledEvent(Id, enrollment.Id, routeId, organizationId, now));
        return enrollment;
    }

    /// <summary>The parent takes the child out of the route.</summary>
    public RouteEnrollment Withdraw(EnrollmentId enrollmentId, DateTimeOffset now) =>
        EndEnrollment(enrollmentId, EnrollmentStatus.CreateWithdrawn(), now, null);

    /// <summary>The transport company ends the enrollment.</summary>
    public RouteEnrollment CancelByOrganization(EnrollmentId enrollmentId, DateTimeOffset now, string? reason) =>
        EndEnrollment(enrollmentId, EnrollmentStatus.CreateCancelled(), now, reason);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>Removes the child. Its enrollments must be ended first (each one frees a seat on a route).</summary>
    public void Remove(DateTimeOffset now)
    {
        EnsureNotRemoved();
        if (_enrollments.Any(e => e.IsActive()))
            throw new StakeholderDomainException(StakeholderError.StudentHasActiveEnrollments,
                "Withdraw the student from its routes before removing it.");
        Status = Removed;
        RemovedAt = now;
        Photo = null;
        _domainEvents.Add(new StudentRemovedEvent(Id, GuardianUserId, now));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private RouteEnrollment EndEnrollment(EnrollmentId enrollmentId, EnrollmentStatus status, DateTimeOffset now,
        string? reason)
    {
        var enrollment = FindEnrollment(enrollmentId)
                         ?? throw new StakeholderDomainException(StakeholderError.EnrollmentNotFound,
                             "The enrollment does not belong to this student.");
        enrollment.End(status, now, reason);
        _domainEvents.Add(new StudentEnrollmentEndedEvent(Id, enrollment.Id, enrollment.RouteId,
            enrollment.OrganizationId, status.Value, now));
        return enrollment;
    }

    private void EnsureNotRemoved()
    {
        if (IsRemoved())
            throw new StakeholderDomainException(StakeholderError.StudentAlreadyRemoved,
                "The student was removed.");
    }

    private static DateOnly? ValidBirthDate(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is null) return null;
        if (birthDate > today.AddYears(-MinimumAgeYears) || birthDate < today.AddYears(-MaximumAgeYears))
            throw new ArgumentException(
                $"The birth date must correspond to a school-age child ({MinimumAgeYears} to {MaximumAgeYears} years).",
                nameof(birthDate));
        return birthDate;
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"The {field} is too long (max {maxLength} characters).", field);
        return trimmed;
    }

    private static string DirectionLabel(string direction) => direction == "PICKUP" ? "pickup" : "return";
}
