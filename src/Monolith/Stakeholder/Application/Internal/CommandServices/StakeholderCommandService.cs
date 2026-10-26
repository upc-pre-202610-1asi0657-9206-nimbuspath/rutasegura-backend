using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.IntegrationEvents;
using Monolith.Stakeholder.Application.Internal.OutboundServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.Entities;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;

namespace Monolith.Stakeholder.Application.Internal.CommandServices;

/// <remarks>
///     Use cases that also change a route (enroll, withdraw, new pickup address, remove) run in one database
///     transaction: Route Planning saves the new stop plan (and its <c>route-updated</c> outbox message) and this
///     service saves the student, so either both changes happen or none.
/// </remarks>
public class StakeholderCommandService(
    IStudentRepository studentRepository,
    IGuardianProfileRepository guardianProfileRepository,
    IStudentPhotoStore photoStore,
    IRoutePlanningService routePlanning,
    IGuardianDirectory directory,
    IStakeholderEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IStakeholderCommandService
{
    // ── Children ─────────────────────────────────────────────────────────────

    public Task<Result<Student>> Handle(RegisterStudentCommand command, CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<Student>(async () =>
        {
            var guardian = RequireGuardian(command.GuardianUserId);
            if (await studentRepository.CountActiveByGuardianAsync(guardian, cancellationToken) >=
                Student.MaxStudentsPerGuardian)
                return Result<Student>.Failure(StakeholderError.StudentLimitReached,
                    $"A guardian can register up to {Student.MaxStudentsPerGuardian} students.");

            var student = new Student(guardian, new FullName(command.FirstName, command.LastName), command.BirthDate,
                command.SchoolGrade, command.SchoolName,
                new PickupLocation(command.PickupAddress, command.PickupReference, command.PickupLatitude,
                    command.PickupLongitude),
                StakeholderOperation.Today(clock), clock.GetUtcNow());

            await studentRepository.AddAsync(student, cancellationToken);
            await SaveAsync(student, cancellationToken);
            return Result<Student>.Success(student);
        });

    public Task<Result<Student>> Handle(UpdateStudentCommand command, CancellationToken cancellationToken) =>
        ChangeStudentAsync(command.GuardianUserId, command.StudentId, student =>
        {
            student.UpdateDetails(new FullName(command.FirstName, command.LastName), command.BirthDate,
                command.SchoolGrade, command.SchoolName, StakeholderOperation.Today(clock));
            return Task.FromResult<Result<Student>?>(null);
        }, cancellationToken);

    /// <summary>Every route the child rides moves it to the stop of the new address (or creates one there).</summary>
    public Task<Result<Student>> Handle(ChangeStudentPickupLocationCommand command,
        CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<Student>(async () =>
        {
            var student = await FindGuardedStudentAsync(command.GuardianUserId, command.StudentId, cancellationToken);
            if (student is null) return StudentNotFound();

            var location = new PickupLocation(command.Address, command.Reference, command.Latitude, command.Longitude);
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            var routesToUpdate = student.ChangePickupLocation(location, clock.GetUtcNow());
            studentRepository.Update(student);

            foreach (var enrollment in routesToUpdate)
            {
                var placement = await routePlanning.PlaceStudentAsync(enrollment.OrganizationId, enrollment.RouteId,
                    student.Id.Identifier, location.Latitude, location.Longitude, location.Address, cancellationToken);
                if (!placement.Succeeded)
                    return Result<Student>.Failure(placement.Error,
                        $"Route '{enrollment.RouteName}' could not use the new address: {placement.Message}");
            }

            await SaveAsync(student, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<Student>.Success(student);
        });

    public Task<Result<Student>> Handle(UploadStudentPhotoCommand command, CancellationToken cancellationToken) =>
        ChangeStudentAsync(command.GuardianUserId, command.StudentId, async student =>
        {
            var content = command.Content ?? [];
            var contentType = StudentPhoto.DetectContentType(content);
            if (contentType is null)
                return Result<Student>.Failure(StakeholderError.StudentPhotoInvalid,
                    "The photo must be a JPEG, PNG or WebP image.");
            if (content.Length > StudentPhoto.MaxSizeBytes)
                return Result<Student>.Failure(StakeholderError.StudentPhotoInvalid,
                    $"The photo must be at most {StudentPhoto.MaxSizeBytes / 1024} KB.");

            student.ReplacePhoto(new StudentPhoto(contentType, content.Length, clock.GetUtcNow()));
            await photoStore.SaveAsync(student.Id, contentType, content, cancellationToken);
            return null;
        }, cancellationToken);

    public Task<Result<Student>> Handle(DeleteStudentPhotoCommand command, CancellationToken cancellationToken) =>
        ChangeStudentAsync(command.GuardianUserId, command.StudentId, async student =>
        {
            student.RemovePhoto();
            await photoStore.DeleteAsync(student.Id, cancellationToken);
            return null;
        }, cancellationToken);

    /// <summary>Frees the child's seat on every route, then removes it (and its photo).</summary>
    public Task<Result<Student>> Handle(RemoveStudentCommand command, CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<Student>(async () =>
        {
            var student = await FindGuardedStudentAsync(command.GuardianUserId, command.StudentId, cancellationToken);
            if (student is null) return StudentNotFound();

            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            foreach (var enrollment in student.ActiveEnrollments)
            {
                var removal = await routePlanning.RemoveStudentAsync(enrollment.OrganizationId, enrollment.RouteId,
                    student.Id.Identifier, cancellationToken);
                if (!removal.Succeeded) return Result<Student>.Failure(removal.Error, removal.Message ?? "");
                student.Withdraw(enrollment.Id, clock.GetUtcNow());
            }

            student.Remove(clock.GetUtcNow());
            studentRepository.Update(student);
            await photoStore.DeleteAsync(student.Id, cancellationToken);
            await SaveAsync(student, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<Student>.Success(student);
        });

    // ── Enrollments ──────────────────────────────────────────────────────────

    public Task<Result<EnrollmentPlacement>> Handle(EnrollStudentCommand command,
        CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<EnrollmentPlacement>(async () =>
        {
            var student = await FindGuardedStudentAsync(command.GuardianUserId, command.StudentId, cancellationToken);
            if (student is null) return Result<EnrollmentPlacement>.Failure(StakeholderError.StudentNotFound,
                "Student was not found.");

            var route = await routePlanning.FindRouteByEnrollmentCodeAsync(command.EnrollmentCode ?? "",
                cancellationToken);
            if (route is null)
                return Result<EnrollmentPlacement>.Failure(StakeholderError.EnrollmentCodeNotFound,
                    "No route uses that enrollment code. Check it with your transport company.");
            if (!route.AcceptsEnrollments)
                return Result<EnrollmentPlacement>.Failure(StakeholderError.RouteNotAcceptingEnrollments,
                    $"Route '{route.Name}' is not accepting new students.");
            if (!await directory.IsOrganizationActiveAsync(route.OrganizationId, cancellationToken))
                return Result<EnrollmentPlacement>.Failure(StakeholderError.OrganizationNotActive,
                    "The transport company of this route is not active.");

            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            var enrollment = student.Enroll(route.RouteId, route.OrganizationId, route.Name, route.Direction,
                clock.GetUtcNow());
            studentRepository.Update(student);

            var placement = await routePlanning.PlaceStudentAsync(route.OrganizationId, route.RouteId,
                student.Id.Identifier, student.PickupLocation.Latitude, student.PickupLocation.Longitude,
                student.PickupLocation.Address, cancellationToken);
            if (!placement.Succeeded)
                return Result<EnrollmentPlacement>.Failure(placement.Error, placement.Message ?? "");

            await SaveAsync(student, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<EnrollmentPlacement>.Success(new EnrollmentPlacement(student, enrollment,
                placement.StopId, placement.StopName, placement.StopSequence));
        });

    public Task<Result<RouteEnrollment>> Handle(WithdrawEnrollmentCommand command,
        CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<RouteEnrollment>(async () =>
        {
            var student = await FindGuardedStudentAsync(command.GuardianUserId, command.StudentId, cancellationToken);
            if (student is null)
                return Result<RouteEnrollment>.Failure(StakeholderError.StudentNotFound, "Student was not found.");
            return await EndEnrollmentAsync(student, new EnrollmentId(command.EnrollmentId),
                s => s.Withdraw(new EnrollmentId(command.EnrollmentId), clock.GetUtcNow()), cancellationToken);
        });

    /// <summary>Enrollments of other organizations are reported as not found.</summary>
    public Task<Result<RouteEnrollment>> Handle(CancelEnrollmentCommand command,
        CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<RouteEnrollment>(async () =>
        {
            if (command.OrganizationId == Guid.Empty || command.EnrollmentId == Guid.Empty) return EnrollmentNotFound();
            var enrollmentId = new EnrollmentId(command.EnrollmentId);
            var student = await studentRepository.FindByEnrollmentIdAsync(enrollmentId, cancellationToken);
            var enrollment = student?.FindEnrollment(enrollmentId);
            if (student is null || enrollment is null || enrollment.OrganizationId != command.OrganizationId)
                return EnrollmentNotFound();

            return await EndEnrollmentAsync(student, enrollmentId,
                s => s.CancelByOrganization(enrollmentId, clock.GetUtcNow(), command.Reason), cancellationToken);
        });

    // ── Guardian ─────────────────────────────────────────────────────────────

    public Task<Result<GuardianProfile>> Handle(UpsertGuardianProfileCommand command,
        CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<GuardianProfile>(async () =>
        {
            var guardian = RequireGuardian(command.GuardianUserId);
            var phone = new GuardianPhoneNumber(command.PhoneNumber);
            var profile = await guardianProfileRepository.FindByUserIdAsync(guardian, cancellationToken);
            if (profile is null)
            {
                profile = new GuardianProfile(guardian, phone, command.DocumentNumber);
                await guardianProfileRepository.AddAsync(profile, cancellationToken);
            }
            else
            {
                profile.Update(phone, command.DocumentNumber);
                guardianProfileRepository.Update(profile);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<GuardianProfile>.Success(profile);
        });

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Result<RouteEnrollment>> EndEnrollmentAsync(Student student, EnrollmentId enrollmentId,
        Func<Student, RouteEnrollment> end, CancellationToken cancellationToken)
    {
        var enrollment = student.FindEnrollment(enrollmentId);
        if (enrollment is null) return EnrollmentNotFound();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        end(student);
        studentRepository.Update(student);
        var removal = await routePlanning.RemoveStudentAsync(enrollment.OrganizationId, enrollment.RouteId,
            student.Id.Identifier, cancellationToken);
        if (!removal.Succeeded) return Result<RouteEnrollment>.Failure(removal.Error, removal.Message ?? "");

        await SaveAsync(student, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<RouteEnrollment>.Success(enrollment);
    }

    private Task<Result<Student>> ChangeStudentAsync(Guid guardianUserId, Guid studentId,
        Func<Student, Task<Result<Student>?>> change, CancellationToken cancellationToken) =>
        StakeholderOperation.ExecuteAsync<Student>(async () =>
        {
            var student = await FindGuardedStudentAsync(guardianUserId, studentId, cancellationToken);
            if (student is null) return StudentNotFound();

            var failure = await change(student);
            if (failure is not null) return failure;

            studentRepository.Update(student);
            await SaveAsync(student, cancellationToken);
            return Result<Student>.Success(student);
        });

    /// <summary>Domain events → outbox, then a single commit.</summary>
    private async Task SaveAsync(Student student, CancellationToken cancellationToken)
    {
        foreach (var integrationEvent in StakeholderIntegrationEventMapper.Map(student.GuardianUserId,
                     student.DomainEvents))
            await outbox.EnqueueAsync(integrationEvent, cancellationToken);
        student.ClearDomainEvents();
        await unitOfWork.CompleteAsync(cancellationToken);
    }

    /// <summary>Children of other guardians (and removed ones) are reported as not found.</summary>
    private async Task<Student?> FindGuardedStudentAsync(Guid guardianUserId, Guid studentId,
        CancellationToken cancellationToken)
    {
        if (guardianUserId == Guid.Empty || studentId == Guid.Empty) return null;
        var student = await studentRepository.FindByStudentIdAsync(new StudentId(studentId), cancellationToken);
        return student is not null && student.IsGuardedBy(guardianUserId) && !student.IsRemoved() ? student : null;
    }

    private static Guid RequireGuardian(Guid guardianUserId) =>
        guardianUserId == Guid.Empty
            ? throw new ArgumentException("The authenticated guardian is required.", nameof(guardianUserId))
            : guardianUserId;

    private static Result<Student> StudentNotFound() =>
        Result<Student>.Failure(StakeholderError.StudentNotFound, "Student was not found.");

    private static Result<RouteEnrollment> EnrollmentNotFound() =>
        Result<RouteEnrollment>.Failure(StakeholderError.EnrollmentNotFound, "Enrollment was not found.");
}
