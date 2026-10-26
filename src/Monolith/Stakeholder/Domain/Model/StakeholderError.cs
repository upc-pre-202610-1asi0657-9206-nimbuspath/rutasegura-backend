namespace Monolith.Stakeholder.Domain.Model;

/// <summary>
///     Failure cases of the Stakeholder bounded context (parents, their children and the enrollment of the children in
///     the routes of a transport company). The interface layer maps them to HTTP status codes; the name travels as the
///     stable <c>code</c> of the problem details.
/// </summary>
public enum StakeholderError
{
    None,

    // ── Students (children) ──────────────────────────────────────────────────
    StudentNotFound,
    StudentLimitReached,
    StudentAlreadyRemoved,
    StudentHasActiveEnrollments,
    StudentPhotoNotFound,
    StudentPhotoInvalid,

    // ── Enrollments ──────────────────────────────────────────────────────────
    EnrollmentCodeNotFound,
    RouteNotFound,
    EnrollmentNotFound,
    StudentAlreadyEnrolled,
    EnrollmentDirectionTaken,
    EnrollmentNotActive,
    RouteNotAcceptingEnrollments,
    RouteFull,
    RouteStopLimitReached,
    OrganizationNotActive,

    // ── Access ───────────────────────────────────────────────────────────────
    RouteNotOperatedByDriver,
    StudentAccessDenied,

    // ── Technical ────────────────────────────────────────────────────────────
    InvalidStakeholderData,
    RoutePlanningUnavailable,
    OperationCancelled,
    InternalServerError
}
