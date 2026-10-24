namespace Monolith.Administration.Domain.Model;

/// <summary>
///     Enumerates the failure cases of the Administration bounded context (Roster and Route Planning).
/// </summary>
/// <remarks>
///     Conveyed through the Result pattern from the application layer to the interface layer, where
///     it is mapped to an HTTP status code and an RFC 7807 problem detail.
/// </remarks>
public enum AdministrationError
{
    None,

    // ── Roster: drivers ──────────────────────────────────────────────────────
    DriverNotFound,
    DriverAlreadyRegistered,
    DriverAccountAlreadyLinked,
    DriverAccountNotValid,
    DriverInvalidTransition,
    DriverLicenseExpired,
    DriverNotAssignable,
    DriverHasActiveRoutes,

    // ── Roster: vehicles ─────────────────────────────────────────────────────
    VehicleNotFound,
    VehiclePlateAlreadyRegistered,
    VehicleInvalidTransition,
    VehicleNotAssignable,
    VehicleHasActiveRoutes,
    VehicleCapacityExceeded,

    // ── Route Planning ───────────────────────────────────────────────────────
    RouteNotFound,
    RouteNameAlreadyExists,
    RouteInvalidTransition,
    RouteNotReadyForActivation,
    RouteStopNotFound,
    RouteStopLimitReached,
    RouteStopTooClose,
    RouteStopHasStudents,
    ActiveRouteRequiresStops,
    StudentNotOnRoute,
    ResourceBelongsToAnotherOrganization,
    ResourceScheduleConflict,
    PlanRouteLimitReached,

    // ── Operations monitoring (events coming from Trip & Tracking) ───────────
    AlertNotFound,
    AlertInvalidTransition,

    // ── Technical ────────────────────────────────────────────────────────────
    InvalidAdministrationData,
    OperationCancelled,
    DatabaseError,
    InternalServerError
}
