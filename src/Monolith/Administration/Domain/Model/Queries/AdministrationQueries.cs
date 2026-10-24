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

/// <summary>How many routes are active and how many the contracted plan allows.</summary>
public record GetActiveRouteQuotaQuery(Guid OrganizationId);

// Operations monitoring
public record GetOperationalAlertsQuery(Guid OrganizationId, bool OnlyOpen);

public record GetTripRecordsByRouteQuery(Guid OrganizationId, Guid RouteId);
