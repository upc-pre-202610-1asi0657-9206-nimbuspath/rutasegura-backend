namespace Monolith.Stakeholder.Domain.Model.Commands;

// Every command of a parent carries its Iam user id (from the JWT, never from the body).

public record RegisterStudentCommand(
    Guid GuardianUserId,
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string? SchoolGrade,
    string? SchoolName,
    string PickupAddress,
    string? PickupReference,
    double PickupLatitude,
    double PickupLongitude);

public record UpdateStudentCommand(
    Guid GuardianUserId,
    Guid StudentId,
    string FirstName,
    string LastName,
    DateOnly? BirthDate,
    string? SchoolGrade,
    string? SchoolName);

/// <summary>The child is re-placed on every route it rides (Route Planning updates the stops).</summary>
public record ChangeStudentPickupLocationCommand(
    Guid GuardianUserId,
    Guid StudentId,
    string Address,
    string? Reference,
    double Latitude,
    double Longitude);

public record UploadStudentPhotoCommand(Guid GuardianUserId, Guid StudentId, byte[] Content);

public record DeleteStudentPhotoCommand(Guid GuardianUserId, Guid StudentId);

/// <summary>Withdraws the child from its routes and removes it.</summary>
public record RemoveStudentCommand(Guid GuardianUserId, Guid StudentId);

/// <summary>The parent enrolls the child in the route the transport company shared the code of.</summary>
public record EnrollStudentCommand(Guid GuardianUserId, Guid StudentId, string EnrollmentCode);

public record WithdrawEnrollmentCommand(Guid GuardianUserId, Guid StudentId, Guid EnrollmentId);

/// <summary>The administrator of the transport company ends an enrollment in one of its routes.</summary>
public record CancelEnrollmentCommand(Guid OrganizationId, Guid EnrollmentId, string? Reason);

public record UpsertGuardianProfileCommand(Guid GuardianUserId, string PhoneNumber, string? DocumentNumber);
