using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Domain.Common;

namespace RutaSegura.TripTracking.Application.Trips.Queries;

/// <summary>Contexto mínimo que Notification necesita para caducar proximidades; no expone GPS ni alumnos.</summary>
public sealed record TripNotificationContextDto(Guid OrganizationId, Guid TripId, bool IsActive);

public sealed record GetTripNotificationContextQuery(Guid TripId, Guid OrganizationId) : IQuery<TripNotificationContextDto>;

internal sealed class GetTripNotificationContextQueryHandler(ITripRepository trips)
    : IQueryHandler<GetTripNotificationContextQuery, TripNotificationContextDto>
{
    public async Task<Result<TripNotificationContextDto>> HandleAsync(GetTripNotificationContextQuery query, CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(new TripId(query.TripId), cancellationToken);
        // Un viaje de otra organización se reporta igual que uno inexistente.
        if (trip is null || trip.OrganizationId.Value != query.OrganizationId) return ApplicationErrors.TripNotFound(query.TripId);
        return new TripNotificationContextDto(query.OrganizationId, query.TripId, trip.IsActive);
    }
}
