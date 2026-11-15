using Microsoft.Extensions.Time.Testing;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;
using Monolith.Stakeholder.Application.IntegrationEvents;
using Monolith.Stakeholder.Application.Internal.CommandServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Application.Internal.QueryServices;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;

namespace RutaSegura.Monolith.Stakeholder.Tests.Support;

/// <summary>Object Mother: a family in Surco and a school route of a transport company.</summary>
internal static class StakeholderMother
{
    /// <summary>Sunday 25 Oct 2026, 10:00 in Lima.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 10, 25);
    public static readonly Guid Guardian = Guid.Parse("b0000000-0000-0000-0000-000000000003");
    public static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static PickupLocation Home() => new("Av. Primavera 1240, Surco", "Puerta verde", -12.11234, -76.98567);

    public static Student NewStudent(Guid? guardian = null) =>
        new(guardian ?? Guardian, new FullName("Valeria", "Quispe"), new DateOnly(2017, 5, 14), "3.° de primaria",
            "Colegio San Agustin", Home(), Today, Now);

    public static RouteInfo Route(string direction = "PICKUP", bool accepts = true, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Organization, $"Ruta {direction}", direction, "ACTIVE", new TimeOnly(6, 45),
            new TimeOnly(7, 50), "America/Lima", ["Monday", "Friday"], 3, 15, accepts);
}

internal sealed class InMemoryStudentRepository : IStudentRepository
{
    public List<Student> Items { get; } = [];

    public Task AddAsync(Student entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<Student?> FindByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<Student?>(null);

    public void Update(Student entity)
    {
    }

    public void Remove(Student entity) => Items.Remove(entity);

    public Task<IEnumerable<Student>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Student>>(Items);

    public Task<Student?> FindByStudentIdAsync(StudentId studentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Id == studentId));

    public Task<IReadOnlyList<Student>> FindActiveByGuardianAsync(Guid guardianUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Student>>(Items.Where(s => s.IsGuardedBy(guardianUserId) && !s.IsRemoved())
            .ToList());

    public Task<int> CountActiveByGuardianAsync(Guid guardianUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Count(s => s.IsGuardedBy(guardianUserId) && !s.IsRemoved()));

    public Task<Student?> FindByEnrollmentIdAsync(EnrollmentId enrollmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(s => s.FindEnrollment(enrollmentId) is not null));

    public Task<IReadOnlyList<Student>> FindEnrolledInOrganizationAsync(Guid organizationId, Guid? routeId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Student>>(Items.Where(s => s.Enrollments.Any(e =>
            e.OrganizationId == organizationId && (routeId is null || e.RouteId == routeId))).ToList());

    public Task<IReadOnlyList<Student>> FindActivelyEnrolledInAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Student>>(Items
            .Where(s => s.ActiveEnrollments.Any(e => routeIds.Contains(e.RouteId))).ToList());
}

internal sealed class InMemoryGuardianProfileRepository : IGuardianProfileRepository
{
    public List<GuardianProfile> Items { get; } = [];

    public Task AddAsync(GuardianProfile entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<GuardianProfile?> FindByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<GuardianProfile?>(null);

    public void Update(GuardianProfile entity)
    {
    }

    public void Remove(GuardianProfile entity) => Items.Remove(entity);

    public Task<IEnumerable<GuardianProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<GuardianProfile>>(Items);

    public Task<GuardianProfile?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(p => p.UserId == userId));

    public Task<IReadOnlyList<GuardianProfile>> FindByUserIdsAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuardianProfile>>(Items.Where(p => userIds.Contains(p.UserId)).ToList());
}

internal sealed class InMemoryPhotoStore : IStudentPhotoStore
{
    public Dictionary<Guid, (string ContentType, byte[] Content)> Photos { get; } = [];

    public Task SaveAsync(StudentId studentId, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        Photos[studentId.Identifier] = (contentType, content);
        return Task.CompletedTask;
    }

    public Task<(string ContentType, byte[] Content)?> FindAsync(StudentId studentId,
        CancellationToken cancellationToken) =>
        Task.FromResult<(string, byte[])?>(Photos.TryGetValue(studentId.Identifier, out var photo) ? photo : null);

    public Task DeleteAsync(StudentId studentId, CancellationToken cancellationToken)
    {
        Photos.Remove(studentId.Identifier);
        return Task.CompletedTask;
    }
}

/// <summary>Route Planning stand-in: records placements and can be told to fail.</summary>
internal sealed class FakeRoutePlanning : IRoutePlanningService
{
    public Dictionary<string, RouteInfo> RoutesByCode { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(Guid RouteId, Guid StudentId, double Latitude, double Longitude)> Placed { get; } = [];
    public List<(Guid RouteId, Guid StudentId)> Removed { get; } = [];
    public Dictionary<Guid, List<Guid>> OperatedRoutes { get; } = [];
    public PlacementOutcome? NextPlacementFailure { get; set; }

    public RouteInfo AddRoute(string code, RouteInfo route)
    {
        RoutesByCode[code] = route;
        return route;
    }

    public Task<RouteInfo?> FindRouteByEnrollmentCodeAsync(string enrollmentCode, CancellationToken cancellationToken) =>
        Task.FromResult(RoutesByCode.GetValueOrDefault(enrollmentCode.Replace("-", "")));

    public Task<IReadOnlyList<RouteInfo>> FindRoutesAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RouteInfo>>(RoutesByCode.Values.Where(r => routeIds.Contains(r.RouteId)).ToList());

    public Task<IReadOnlyList<StudentStop>> FindStudentStopsAsync(IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StudentStop>>(Placed
            .Where(p => routeIds.Contains(p.RouteId) && !Removed.Contains((p.RouteId, p.StudentId)))
            .Select(p => new StudentStop(p.RouteId, p.StudentId, Guid.Empty, "Parada", null, 1, p.Latitude, p.Longitude))
            .ToList());

    public Task<IReadOnlyList<Guid>> FindRouteIdsOperatedByAsync(Guid organizationId, Guid driverUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(OperatedRoutes.GetValueOrDefault(driverUserId) ?? []);

    public Task<PlacementOutcome> PlaceStudentAsync(Guid organizationId, Guid routeId, Guid studentId, double latitude,
        double longitude, string address, CancellationToken cancellationToken)
    {
        if (NextPlacementFailure is { } failure)
        {
            NextPlacementFailure = null;
            return Task.FromResult(failure);
        }

        Placed.Add((routeId, studentId, latitude, longitude));
        return Task.FromResult(new PlacementOutcome(true, Guid.NewGuid(), address, 1, StakeholderError.None, null));
    }

    public Task<PlacementOutcome> RemoveStudentAsync(Guid organizationId, Guid routeId, Guid studentId,
        CancellationToken cancellationToken)
    {
        Removed.Add((routeId, studentId));
        return Task.FromResult(new PlacementOutcome(true, null, null, null, StakeholderError.None, null));
    }
}

internal sealed class FakeDirectory : IGuardianDirectory
{
    public bool OrganizationActive { get; set; } = true;

    public Task<GuardianAccount?> FindAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<GuardianAccount?>(new GuardianAccount(userId, "Rosa Quispe", "rosa@familia.pe", "PARENT"));

    public Task<string?> FindOrganizationNameAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("Movilidad Sur");

    public Task<bool> IsOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(OrganizationActive);
}

internal sealed class RecordingOutbox : IStakeholderEventOutbox
{
    public List<StakeholderIntegrationEvent> Events { get; } = [];

    public Task EnqueueAsync(StakeholderIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        Events.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Counts commits and transactions; a transaction disposed without commit is a rollback.</summary>
internal sealed class TransactionalUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }
    public int CommittedTransactions { get; private set; }
    public int RolledBackTransactions { get; private set; }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Commits++;
        return Task.CompletedTask;
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));

    private sealed class Transaction(TransactionalUnitOfWork owner) : IUnitOfWorkTransaction
    {
        private bool _committed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _committed = true;
            owner.CommittedTransactions++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed) owner.RolledBackTransactions++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class StakeholderFixture
{
    public InMemoryStudentRepository Students { get; } = new();
    public InMemoryGuardianProfileRepository Profiles { get; } = new();
    public InMemoryPhotoStore Photos { get; } = new();
    public FakeRoutePlanning RoutePlanning { get; } = new();
    public FakeDirectory Directory { get; } = new();
    public RecordingOutbox Outbox { get; } = new();
    public TransactionalUnitOfWork UnitOfWork { get; } = new();
    public FakeTimeProvider Clock { get; } = new(StakeholderMother.Now);

    public StakeholderCommandService Commands =>
        new(Students, Profiles, Photos, RoutePlanning, Directory, Outbox, UnitOfWork, Clock);

    public StakeholderQueryService Queries => new(Students, Profiles, Photos, RoutePlanning, Directory);

    public Student AddStudent(Guid? guardian = null)
    {
        var student = StakeholderMother.NewStudent(guardian);
        student.ClearDomainEvents();
        Students.Items.Add(student);
        return student;
    }
}
