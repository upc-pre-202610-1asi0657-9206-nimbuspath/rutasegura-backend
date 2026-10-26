namespace Monolith.Administration.Domain.Model.Queries;

// Every query is scoped to the organization of the authenticated administrator.

// Roster
public record GetDriverByIdQuery(Guid OrganizationId, Guid DriverId);

public record GetAllDriversQuery(Guid OrganizationId);

/// <summary>Profile of the authenticated driver, found by the IAM user id of the token.</summary>
public record GetDriverByUserIdQuery(Guid OrganizationId, Guid UserId);

public record GetVehicleByIdQuery(Guid OrganizationId, Guid VehicleId);

public record GetAllVehiclesQuery(Guid OrganizationId);

// Route Planning
public record GetRouteByIdQuery(Guid OrganizationId, Guid RouteId);

public record GetAllRoutesQuery(Guid OrganizationId);

public record GetRoutesByDriverQuery(Guid OrganizationId, Guid DriverId);

/// <summary>Route families enroll in with a code (any organization: the code itself is the secret).</summary>
public record GetRouteByEnrollmentCodeQuery(string EnrollmentCode);

/// <summary>Routes by id, any organization. Only for the published facade (callers scope the result themselves).</summary>
public record GetRoutesByIdsQuery(IReadOnlyCollection<Guid> RouteIds);

/// <summary>How many routes are active and how many the contracted plan allows.</summary>
public record GetActiveRouteQuotaQuery(Guid OrganizationId);

// Operations monitoring
public record GetOperationalAlertsQuery(Guid OrganizationId, bool OnlyOpen);

public record GetTripRecordsByRouteQuery(Guid OrganizationId, Guid RouteId);
