namespace Monolith.Stakeholder.Interfaces.Rest.Resources;

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>Where the child is picked up: the address, an optional reference and the point chosen on the map.</summary>
public record PickupLocationResource(string Address, string? Reference, double Latitude, double Longitude);

public record RegisterStudentResource(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string? SchoolGrade,
    string? SchoolName,
    PickupLocationResource PickupLocation);

public record UpdateStudentResource(
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string? SchoolGrade,
    string? SchoolName);

/// <summary>Code the transport company shared with the families of the route (e.g. "K7QM-4P2X").</summary>
public record EnrollStudentResource(string EnrollmentCode);

public record CancelEnrollmentResource(string? Reason);

public record UpsertGuardianProfileResource(string PhoneNumber, string? DocumentNumber);

// ── Responses ────────────────────────────────────────────────────────────────

/// <summary>The photo is served by <c>GET /api/v1/stakeholders/students/{id}/photo</c> with the token (never public).</summary>
public record StudentPhotoResource(string Url, string ContentType, int SizeBytes, DateTimeOffset UploadedAt,
    string Version);

/// <summary>Stop of the route where the child is picked up (PICKUP) or dropped off (RETURN).</summary>
public record StudentStopResource(Guid StopId, string Name, string? Address, int Sequence, double Latitude,
    double Longitude);

/// <param name="RouteStatus">DRAFT | ACTIVE | INACTIVE; null when the route was deleted.</param>
/// <param name="Stop">Null when the child is not on a stop of the route (route deleted or child removed by the company).</param>
public record EnrollmentResource(
    Guid Id,
    Guid RouteId,
    string RouteName,
    Guid OrganizationId,
    string? OrganizationName,
    string Direction,
    string Status,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? EndedAt,
    string? EndReason,
    string? RouteStatus,
    TimeOnly? DepartureTime,
    TimeOnly? EstimatedArrivalTime,
    IReadOnlyList<string>? OperatingDays,
    StudentStopResource? Stop);

public record StudentResource(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    DateOnly? BirthDate,
    string? SchoolGrade,
    string? SchoolName,
    PickupLocationResource PickupLocation,
    StudentPhotoResource? Photo,
    string Status,
    IReadOnlyList<EnrollmentResource> Enrollments,
    DateTimeOffset? CreatedAt);

public record RouteOfferResource(
    Guid RouteId,
    string RouteName,
    string? OrganizationName,
    string Direction,
    string Status,
    TimeOnly DepartureTime,
    TimeOnly EstimatedArrivalTime,
    string TimeZoneId,
    IReadOnlyList<string> OperatingDays,
    int? AvailableSeats,
    bool AcceptsEnrollments);

/// <summary>Contact of the parent. The e-mail is only shown to the administrator.</summary>
public record GuardianContactResource(Guid UserId, string? FullName, string? PhoneNumber, string? Email);

/// <summary>A child riding a route, as the administrator and the driver see it.</summary>
public record RouteStudentResource(
    Guid StudentId,
    Guid EnrollmentId,
    Guid RouteId,
    string RouteName,
    string FirstName,
    string LastName,
    string FullName,
    string? SchoolGrade,
    string? SchoolName,
    string? PhotoUrl,
    PickupLocationResource PickupLocation,
    StudentStopResource? Stop,
    GuardianContactResource Guardian,
    string EnrollmentStatus,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? EndedAt,
    string? EndReason);

public record GuardianProfileResource(
    Guid UserId,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    string? DocumentNumber,
    bool IsComplete);
