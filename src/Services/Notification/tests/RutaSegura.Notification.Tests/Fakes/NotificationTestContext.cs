using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Domain.Model.Aggregates;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;
using RutaSegura.Notification.Domain.Model.Entities;

namespace RutaSegura.Notification.Tests.Fakes;

// Dobles exclusivos de pruebas. No se registran en el host productivo.
public abstract class NotificationTestContext : IDisposable
{
    protected static readonly Guid Organization = Guid.Parse("10000000-0000-0000-0000-000000000001");
    protected static readonly Guid OtherOrganization = Guid.Parse("10000000-0000-0000-0000-000000000002");
    protected static readonly Guid Guardian = Guid.Parse("20000000-0000-0000-0000-000000000001");
    protected static readonly Guid OtherGuardian = Guid.Parse("20000000-0000-0000-0000-000000000002");
    protected static readonly Guid Operator = Guid.Parse("20000000-0000-0000-0000-000000000003");
    protected static readonly Guid Student = Guid.Parse("30000000-0000-0000-0000-000000000001");
    protected static readonly Guid OtherStudent = Guid.Parse("30000000-0000-0000-0000-000000000002");
    protected static readonly Guid Trip = Guid.Parse("40000000-0000-0000-0000-000000000001");
    protected static readonly Guid Stop = Guid.Parse("50000000-0000-0000-0000-000000000001");
    protected static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal MemoryStore Store { get; } = new();
    internal DirectoryFake Directory { get; } = new();
    internal UserContextFake User { get; } = new() { Current = new NotificationUser(Guardian, Organization) };
    internal UnitOfWorkFake UnitOfWork { get; } = new();
    internal ClockFake Clock { get; } = new(Now);
    protected INotificationCommandService Commands { get; }
    protected INotificationQueryService Queries { get; }
    private readonly ServiceProvider _provider;

    protected NotificationTestContext()
    {
        Directory.Students.Add(new(Organization, Guardian, Student));
        Directory.Operators.Add(new(Organization, Operator));
        Directory.AnnouncementRecipients.Add(new(Organization, Guardian));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<INotificationRepository>(Store);
        services.AddSingleton<IAnnouncementRepository>(Store);
        services.AddSingleton<IProcessedNotificationEventRepository>(Store);
        services.AddSingleton<INotificationDispatchQueue>(Store);
        services.AddSingleton<INotificationRecipientDirectory>(Directory);
        services.AddSingleton<ITripNotificationContext>(Directory);
        services.AddSingleton<INotificationUserContext>(User);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddNotificationApplication();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Commands = _provider.GetRequiredService<INotificationCommandService>();
        Queries = _provider.GetRequiredService<INotificationQueryService>();
    }

    protected ReceiveTripEventCommand Event(TripNotificationEventV1? payload = null, Guid? messageId = null,
        DateTimeOffset? occurredAt = null)
    {
        payload ??= new StudentBoardedV1(Organization, Trip, Student, Stop);
        return new(new(messageId ?? Guid.NewGuid(), TripNotificationContracts.MessageType(payload), 1,
            occurredAt ?? Now, "test-correlation"), payload);
    }

    protected NotificationEntity Notice(Guid? recipient = null, Guid? organization = null, Guid? student = null,
        NotificationCategory category = NotificationCategory.Boarding)
    {
        var notice = NotificationEntity.Create(organization ?? Organization, recipient ?? Guardian, Guid.NewGuid(),
            "test.event.v1", "test-correlation", category, NotificationContent.Create("Aviso", "Contenido permitido"),
            Now, Now, tripId: Trip, studentId: student);
        Store.Add(notice);
        return notice;
    }

    public void Dispose() => _provider.Dispose();
}

internal sealed class ClockFake(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class UserContextFake : INotificationUserContext
{
    public NotificationUser? Current { get; set; }
}

internal sealed class UnitOfWorkFake : IUnitOfWork
{
    public int Commits { get; private set; }
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commits++;
        return Task.CompletedTask;
    }
}

internal sealed class MemoryStore : INotificationRepository, IAnnouncementRepository, INotificationDispatchQueue, IProcessedNotificationEventRepository
{
    public List<NotificationEntity> Notifications { get; } = [];
    public List<Announcement> Announcements { get; } = [];
    public List<Guid> PendingDispatch { get; } = [];
    public List<ProcessedNotificationEvent> ProcessedEvents { get; } = [];
    public Task<ProcessedNotificationEvent?> FindAsync(string consumer, Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult(ProcessedEvents.SingleOrDefault(e => e.Consumer == consumer && e.MessageId == messageId));
    public void Add(ProcessedNotificationEvent processedEvent) => ProcessedEvents.Add(processedEvent);
    public Task<NotificationEntity?> GetAsync(Guid organizationId, Guid notificationId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(n => n.OrganizationId == organizationId && n.Id == notificationId));
    public Task<NotificationEntity?> FindByKeyAsync(NotificationKey key, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(n => n.Key == key));
    public Task<IReadOnlyList<NotificationEntity>> ListAsync(InboxFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationEntity>>(Notifications.Where(n =>
                n.OrganizationId == filter.OrganizationId && n.RecipientUserId == filter.RecipientUserId &&
                (n.StudentId is null || filter.AuthorizedStudentIds.Contains(n.StudentId.Value)) &&
                (!filter.UnreadOnly || n.ReadAt is null))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip(filter.Offset).Take(filter.Limit).ToArray());
    public void Add(NotificationEntity notification) => Notifications.Add(notification);
    public Task<Announcement?> FindByRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Announcements.SingleOrDefault(a => a.OrganizationId == organizationId && a.RequestId == requestId));
    public void Add(Announcement announcement) => Announcements.Add(announcement);
    public void Enqueue(NotificationEntity notification) => PendingDispatch.Add(notification.Id);
}

internal sealed class DirectoryFake : INotificationRecipientDirectory, ITripNotificationContext
{
    public List<NotificationRecipient> Students { get; } = [];
    public List<NotificationRecipient> Operators { get; } = [];
    public List<NotificationRecipient> AnnouncementRecipients { get; } = [];
    public bool TripActive { get; set; } = true;
    public bool Unavailable { get; set; }
    public bool ReturnUnfilteredRecipients { get; set; }
    public Error? AnnouncementError { get; set; }

    public Task<IReadOnlyList<NotificationRecipient>> ForStudentsAsync(Guid organizationId, IReadOnlyCollection<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException("Directorio no disponible");
        return Task.FromResult<IReadOnlyList<NotificationRecipient>>(ReturnUnfilteredRecipients ? Students.ToArray() :
            Students.Where(r => r.OrganizationId == organizationId && r.StudentId is not null && studentIds.Contains(r.StudentId.Value)).ToArray());
    }
    Task<IReadOnlyList<NotificationRecipient>> INotificationRecipientDirectory.OperatorsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationRecipient>>(Operators.ToArray());
    public Task<Result<IReadOnlyList<NotificationRecipient>>> ForAnnouncementAsync(Guid organizationId, Guid? routeId, CancellationToken cancellationToken) =>
        Task.FromResult(AnnouncementError is not null ? Result<IReadOnlyList<NotificationRecipient>>.Failure(AnnouncementError) :
            Result<IReadOnlyList<NotificationRecipient>>.Success(AnnouncementRecipients.ToArray()));
    public Task<IReadOnlyCollection<Guid>> CurrentStudentLinksAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new TimeoutException("Directorio no disponible");
        return Task.FromResult<IReadOnlyCollection<Guid>>(Students.Where(r => r.OrganizationId == organizationId && r.UserId == userId)
            .Select(r => r.StudentId!.Value).Distinct().ToArray());
    }
    public Task<bool> IsActiveAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken) => Task.FromResult(TripActive);
}
