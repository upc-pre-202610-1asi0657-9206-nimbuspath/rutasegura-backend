using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Entities;

namespace Monolith.Stakeholder.Application.QueryServices;

/// <summary>An enrollment with the current state of its route and the stop where the child is picked up.</summary>
/// <param name="Route">Null when the route was deleted.</param>
/// <param name="Stop">Null when the child is not on a stop (route deleted or child removed by the administrator).</param>
public sealed record EnrollmentDetails(
    RouteEnrollment Enrollment,
    RouteInfo? Route,
    StudentStop? Stop,
    string? OrganizationName);

public sealed record StudentDetails(Student Student, IReadOnlyList<EnrollmentDetails> Enrollments);

public sealed record RouteOffer(RouteInfo Route, string? OrganizationName);

public sealed record GuardianProfileDetails(Guid UserId, GuardianAccount? Account, GuardianProfile? Profile);

/// <summary>A child riding a route, as the administrator and the driver see it.</summary>
public sealed record RouteStudentDetails(
    Student Student,
    RouteEnrollment Enrollment,
    StudentStop? Stop,
    GuardianAccount? Guardian,
    GuardianProfile? GuardianProfile);

public sealed record StudentPhotoContent(string ContentType, byte[] Content, string Version);
