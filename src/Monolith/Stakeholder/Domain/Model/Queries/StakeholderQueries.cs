namespace Monolith.Stakeholder.Domain.Model.Queries;

public record GetMyStudentsQuery(Guid GuardianUserId);

public record GetMyStudentQuery(Guid GuardianUserId, Guid StudentId);

public record GetGuardianProfileQuery(Guid GuardianUserId);

/// <summary>Preview of the route behind an enrollment code, before the parent enrolls.</summary>
public record GetRouteOfferQuery(string EnrollmentCode);

/// <summary>Enrollments in the routes of an organization (administrator).</summary>
public record GetOrganizationEnrollmentsQuery(Guid OrganizationId, Guid? RouteId, bool OnlyActive);

/// <summary>Children riding a route: for its administrator or for the driver who operates it.</summary>
public record GetRouteStudentsQuery(Guid OrganizationId, Guid RequesterUserId, string RequesterRole, Guid RouteId);

/// <summary>Children of every route the authenticated driver operates (offline cache of the mobile app).</summary>
public record GetDriverStudentsQuery(Guid OrganizationId, Guid DriverUserId);

/// <summary>Photo bytes; the requester must be the guardian or staff of a route the child rides.</summary>
public record GetStudentPhotoQuery(Guid StudentId, Guid RequesterUserId, string RequesterRole, Guid? OrganizationId);
