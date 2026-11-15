using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Administration.Application.Internal.CommandServices;

/// <remarks>
///     The record handlers are invoked by the RabbitMQ consumer (infrastructure) for messages of
///     <c>trip-tracking.events</c>. The consumer also keeps an Inbox by message id; these handlers are
///     idempotent on their own (incident id, one open SOS per trip, one record per trip) as a second barrier.
/// </remarks>
public class OperationsMonitoringCommandService(
    IOperationalAlertRepository alertRepository,
    ITripRecordRepository tripRecordRepository,
    IRouteRepository routeRepository,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IOperationsMonitoringCommandService
{
    // ── Events from Trip & Tracking ──────────────────────────────────────────

    public Task<Result<OperationalAlert>> Handle(RecordIncidentReportedCommand command,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<OperationalAlert>(async () =>
        {
            var existing = await alertRepository.FindByIncidentIdAsync(command.IncidentId, cancellationToken);
            if (existing is not null) return Result<OperationalAlert>.Success(existing);

            var origin = await ResolveOriginAsync(command.OrganizationId, command.RouteId, command.RouteName,
                cancellationToken);
            if (origin is null) return UnknownOrigin();

            var alert = OperationalAlert.FromIncident(
                origin.Value.OrganizationId,
                new TripId(command.TripId),
                new RouteId(command.RouteId),
                origin.Value.RouteName,
                command.IncidentId,
                IncidentCategory.FromTripTrackingType(command.IncidentType),
                command.Detail,
                AdministrationOperation.TryLocation(command.Latitude, command.Longitude),
                command.AffectedStudents,
                command.OccurredAt);

            await alertRepository.AddAsync(alert, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<OperationalAlert>.Success(alert);
        });

    public Task<Result<OperationalAlert>> Handle(RecordSosRaisedCommand command,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<OperationalAlert>(async () =>
        {
            var tripId = new TripId(command.TripId);
            var latest = await alertRepository.FindLatestSosByTripIdAsync(tripId, cancellationToken);
            if (latest is not null && latest.IsOpen()) return Result<OperationalAlert>.Success(latest);

            var origin = await ResolveOriginAsync(command.OrganizationId, command.RouteId, command.RouteName,
                cancellationToken);
            if (origin is null) return UnknownOrigin();

            var alert = OperationalAlert.FromSos(
                origin.Value.OrganizationId,
                tripId,
                new RouteId(command.RouteId),
                origin.Value.RouteName,
                command.DriverUserId,
                command.VehicleId,
                AdministrationOperation.TryLocation(command.Latitude, command.Longitude),
                command.StudentsOnBoard,
                command.OccurredAt);

            await alertRepository.AddAsync(alert, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<OperationalAlert>.Success(alert);
        });

    public Task<Result<OperationalAlert>> Handle(RecordSosResolvedCommand command,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<OperationalAlert>(async () =>
        {
            var alert = await alertRepository.FindLatestSosByTripIdAsync(new TripId(command.TripId),
                cancellationToken);
            if (alert is null)
                return Result<OperationalAlert>.Failure(AdministrationError.AlertNotFound,
                    "No SOS alert was recorded for the trip.");
            if (!alert.IsOpen()) return Result<OperationalAlert>.Success(alert);

            alert.ResolveFromTripTracking(command.ResolvedBy, command.OccurredAt);
            alertRepository.Update(alert);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<OperationalAlert>.Success(alert);
        });

    public Task<Result<TripRecord>> Handle(RecordTripCompletedCommand command,
        CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<TripRecord>(async () =>
        {
            var tripId = new TripId(command.TripId);
            var existing = await tripRecordRepository.FindByTripIdAsync(tripId, cancellationToken);
            if (existing is not null) return Result<TripRecord>.Success(existing);

            var origin = await ResolveOriginAsync(command.OrganizationId, command.RouteId, command.RouteName,
                cancellationToken);
            if (origin is null)
                return Result<TripRecord>.Failure(AdministrationError.RouteNotFound,
                    "The trip belongs to a route unknown to Administration and carries no organization.");

            var tripRecord = new TripRecord(origin.Value.OrganizationId, tripId, new RouteId(command.RouteId),
                origin.Value.RouteName, command.DeliveredStudents, command.AbsentStudents, command.IncidentCount,
                command.StartedAt, command.CompletedAt);

            await tripRecordRepository.AddAsync(tripRecord, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<TripRecord>.Success(tripRecord);
        });

    // ── Administrator follow-up ──────────────────────────────────────────────

    public Task<Result<OperationalAlert>> Handle(AcknowledgeAlertCommand command,
        CancellationToken cancellationToken) =>
        ChangeAlertAsync(command.OrganizationId, command.AlertId, command.AdministratorUserId,
            (alert, administrator) => alert.Acknowledge(administrator, clock.GetUtcNow()), cancellationToken);

    public Task<Result<OperationalAlert>> Handle(ResolveAlertCommand command, CancellationToken cancellationToken) =>
        ChangeAlertAsync(command.OrganizationId, command.AlertId, command.AdministratorUserId,
            (alert, administrator) => alert.ResolveByAdministrator(administrator, clock.GetUtcNow()),
            cancellationToken);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<Result<OperationalAlert>> ChangeAlertAsync(Guid organizationId, Guid alertId,
        Guid administratorUserId, Action<OperationalAlert, UserId> change, CancellationToken cancellationToken) =>
        AdministrationOperation.ExecuteAsync<OperationalAlert>(async () =>
        {
            var organization = AdministrationOperation.Organization(organizationId);
            if (administratorUserId == Guid.Empty)
                throw new ArgumentException("The administrator is required.", nameof(administratorUserId));

            var alert = await alertRepository.FindByAlertIdAsync(new OperationalAlertId(alertId), cancellationToken);
            if (alert is null || alert.OrganizationId != organization)
                return Result<OperationalAlert>.Failure(AdministrationError.AlertNotFound, "Alert was not found.");

            change(alert, new UserId(administratorUserId));
            alertRepository.Update(alert);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<OperationalAlert>.Success(alert);
        });

    /// <summary>
    ///     Organization and route name of the trip: taken from the event when present, otherwise from the route
    ///     planned in Administration (events of demo trips may not carry the organization).
    /// </summary>
    private async Task<(OrganizationId OrganizationId, string? RouteName)?> ResolveOriginAsync(Guid? organizationId,
        Guid routeId, string? routeName, CancellationToken cancellationToken)
    {
        var route = routeId == Guid.Empty
            ? null
            : await routeRepository.FindByRouteIdAsync(new RouteId(routeId), cancellationToken);
        var name = string.IsNullOrWhiteSpace(routeName) ? route?.Name.Value : routeName;

        if (organizationId is { } id && id != Guid.Empty) return (new OrganizationId(id), name);
        if (route is not null) return (route.OrganizationId, name);
        return null;
    }

    private static Result<OperationalAlert> UnknownOrigin() =>
        Result<OperationalAlert>.Failure(AdministrationError.RouteNotFound,
            "The trip belongs to a route unknown to Administration and carries no organization.");
}
