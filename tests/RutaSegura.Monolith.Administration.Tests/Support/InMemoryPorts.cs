using Monolith.Administration.Application.IntegrationEvents;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Application.Internal.OutboundServices.ACL;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Repositories;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace RutaSegura.Monolith.Administration.Tests.Support;

// Test doubles of the ports used by the application layer. Repositories keep aggregates in memory;
// the unit of work only counts commits, so a test can assert that a rejected use case saved nothing.

internal abstract class InMemoryRepository<TEntity> : IBaseRepository<TEntity> where TEntity : class
{
    public List<TEntity> Items { get; } = [];

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<TEntity?> FindByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<TEntity?>(null);

    public void Update(TEntity entity)
    {
    }

    public void Remove(TEntity entity) => Items.Remove(entity);

    public Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<TEntity>>(Items.ToList());

    protected Task<TEntity?> Single(Func<TEntity, bool> predicate) =>
        Task.FromResult(Items.FirstOrDefault(predicate));

    protected Task<IEnumerable<TEntity>> Many(Func<TEntity, bool> predicate) =>
        Task.FromResult<IEnumerable<TEntity>>(Items.Where(predicate).ToList());
}

internal sealed class InMemoryDriverRepository : InMemoryRepository<Driver>, IDriverRepository
{
    public Task<Driver?> FindByDriverIdAsync(DriverId driverId, CancellationToken cancellationToken) =>
        Single(d => d.Id == driverId);

    public Task<IEnumerable<Driver>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) => Many(d => d.OrganizationId == organizationId);

    public Task<bool> ExistsByDocumentNumberAsync(OrganizationId organizationId, DocumentNumber documentNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(d => d.OrganizationId == organizationId && d.DocumentNumber == documentNumber));

    public Task<bool> ExistsByUserIdAsync(UserId userId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(d => d.UserId == userId));

    public Task<Driver?> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken) =>
        Single(d => d.UserId == userId);
}

internal sealed class InMemoryVehicleRepository : InMemoryRepository<Vehicle>, IVehicleRepository
{
    public Task<Vehicle?> FindByVehicleIdAsync(VehicleId vehicleId, CancellationToken cancellationToken) =>
        Single(v => v.Id == vehicleId);

    public Task<IEnumerable<Vehicle>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) => Many(v => v.OrganizationId == organizationId);

    public Task<bool> ExistsByLicensePlateAsync(OrganizationId organizationId, LicensePlate licensePlate,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(v => v.OrganizationId == organizationId && v.LicensePlate == licensePlate));
}

internal sealed class InMemoryRouteRepository : InMemoryRepository<Route>, IRouteRepository
{
    public Task<Route?> FindByRouteIdAsync(RouteId routeId, CancellationToken cancellationToken) =>
        Single(r => r.Id == routeId);

    public Task<IEnumerable<Route>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) => Many(r => r.OrganizationId == organizationId);

    public Task<IEnumerable<Route>> FindAllByDriverIdAsync(DriverId driverId, CancellationToken cancellationToken) =>
        Many(r => r.UsesDriver(driverId));

    public Task<IEnumerable<Route>> FindAllByVehicleIdAsync(VehicleId vehicleId,
        CancellationToken cancellationToken) => Many(r => r.UsesVehicle(vehicleId));

    public Task<int> CountActiveByOrganizationIdAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.Count(r => r.OrganizationId == organizationId && r.IsActive()));

    public Task<Route?> FindByEnrollmentCodeAsync(RouteEnrollmentCode code, CancellationToken cancellationToken) =>
        Single(r => r.EnrollmentCode == code);

    public Task<IEnumerable<Route>> FindAllByIdsAsync(IReadOnlyCollection<RouteId> routeIds,
        CancellationToken cancellationToken) => Many(r => routeIds.Contains(r.Id));

    public Task<bool> ExistsByNameAsync(OrganizationId organizationId, RouteName name,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(r => r.OrganizationId == organizationId &&
                                       string.Equals(r.Name.Value, name.Value, StringComparison.OrdinalIgnoreCase)));
}

internal sealed class InMemoryOperationalAlertRepository : InMemoryRepository<OperationalAlert>,
    IOperationalAlertRepository
{
    public Task<OperationalAlert?> FindByAlertIdAsync(OperationalAlertId alertId,
        CancellationToken cancellationToken) => Single(a => a.Id == alertId);

    public Task<OperationalAlert?> FindByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken) =>
        Single(a => a.IncidentId == incidentId);

    public Task<OperationalAlert?> FindLatestSosByTripIdAsync(TripId tripId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Where(a => a.TripId == tripId && a.Type.IsSos())
            .OrderByDescending(a => a.OccurredAt).FirstOrDefault());

    public Task<IEnumerable<OperationalAlert>> FindAllByOrganizationIdAsync(OrganizationId organizationId,
        bool onlyOpen, CancellationToken cancellationToken) =>
        Many(a => a.OrganizationId == organizationId && (!onlyOpen || a.IsOpen()));
}

internal sealed class InMemoryTripRecordRepository : InMemoryRepository<TripRecord>, ITripRecordRepository
{
    public Task<TripRecord?> FindByTripIdAsync(TripId tripId, CancellationToken cancellationToken) =>
        Single(r => r.TripId == tripId);

    public Task<IEnumerable<TripRecord>> FindAllByRouteIdAsync(RouteId routeId,
        CancellationToken cancellationToken) => Many(r => r.RouteId == routeId);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Commits++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingOutbox : IIntegrationEventOutbox
{
    public List<IntegrationEvent> Events { get; } = [];

    public Task EnqueueAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Events.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> MessageTypes => Events.Select(e => e.MessageType).ToList();
}

internal sealed class StubPlanLimitsProvider : IPlanLimitsProvider
{
    public PlanLimits Limits { get; set; } = AdministrationMother.Plan();

    public Task<PlanLimits> GetCurrentLimitsAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) => Task.FromResult(Limits);
}

internal sealed class StubIamAccountService : IIamAccountService
{
    public bool IsDriverAccount { get; set; } = true;

    public Task<bool> IsDriverAccountOfOrganizationAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken) => Task.FromResult(IsDriverAccount);
}
