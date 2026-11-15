using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Shared.Application.Model;

namespace Monolith.Administration.Application.CommandServices;

/// <summary>
///     Reactions to the events Trip &amp; Tracking publishes (incidents, SOS, completed trips) and the
///     administrator's follow-up of the resulting alerts. All event handlers are idempotent.
/// </summary>
public interface IOperationsMonitoringCommandService
{
    Task<Result<OperationalAlert>> Handle(RecordIncidentReportedCommand command, CancellationToken cancellationToken);

    Task<Result<OperationalAlert>> Handle(RecordSosRaisedCommand command, CancellationToken cancellationToken);

    Task<Result<OperationalAlert>> Handle(RecordSosResolvedCommand command, CancellationToken cancellationToken);

    Task<Result<TripRecord>> Handle(RecordTripCompletedCommand command, CancellationToken cancellationToken);

    Task<Result<OperationalAlert>> Handle(AcknowledgeAlertCommand command, CancellationToken cancellationToken);

    Task<Result<OperationalAlert>> Handle(ResolveAlertCommand command, CancellationToken cancellationToken);
}
