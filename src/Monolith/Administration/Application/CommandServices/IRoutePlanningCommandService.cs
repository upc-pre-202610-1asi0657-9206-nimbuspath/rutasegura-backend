using Monolith.Administration.Domain.Model.Commands;
using Monolith.Shared.Application.Model;
// Microsoft.AspNetCore.Routing (implicit global using of the Web SDK) also defines Route.
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;

namespace Monolith.Administration.Application.CommandServices;

/// <summary>Route Planning use cases: routes, stops, students per stop, assignments and lifecycle.</summary>
public interface IRoutePlanningCommandService
{
    Task<Result<Route>> Handle(CreateRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(RenameRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(RescheduleRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(AddRouteStopCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(UpdateRouteStopCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(RemoveRouteStopCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(ReorderRouteStopsCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(AssignStudentToStopCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(UnassignStudentFromRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(AssignRouteResourcesCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(ReleaseRouteResourcesCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(ActivateRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(DeactivateRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(DeleteRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(PlaceStudentOnRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(RemoveStudentFromRouteCommand command, CancellationToken cancellationToken);

    Task<Result<Route>> Handle(RegenerateRouteEnrollmentCodeCommand command, CancellationToken cancellationToken);
}
