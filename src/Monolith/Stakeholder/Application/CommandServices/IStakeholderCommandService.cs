using Monolith.Shared.Application.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.Entities;

namespace Monolith.Stakeholder.Application.CommandServices;

/// <summary>Use cases of the parents (children, photo, pickup address, enrollments) and of the transport company.</summary>
public interface IStakeholderCommandService
{
    Task<Result<Student>> Handle(RegisterStudentCommand command, CancellationToken cancellationToken);

    Task<Result<Student>> Handle(UpdateStudentCommand command, CancellationToken cancellationToken);

    Task<Result<Student>> Handle(ChangeStudentPickupLocationCommand command, CancellationToken cancellationToken);

    Task<Result<Student>> Handle(UploadStudentPhotoCommand command, CancellationToken cancellationToken);

    Task<Result<Student>> Handle(DeleteStudentPhotoCommand command, CancellationToken cancellationToken);

    Task<Result<Student>> Handle(RemoveStudentCommand command, CancellationToken cancellationToken);

    Task<Result<EnrollmentPlacement>> Handle(EnrollStudentCommand command, CancellationToken cancellationToken);

    Task<Result<RouteEnrollment>> Handle(WithdrawEnrollmentCommand command, CancellationToken cancellationToken);

    Task<Result<RouteEnrollment>> Handle(CancelEnrollmentCommand command, CancellationToken cancellationToken);

    Task<Result<GuardianProfile>> Handle(UpsertGuardianProfileCommand command, CancellationToken cancellationToken);
}

/// <summary>The new enrollment and the stop Route Planning placed the child on.</summary>
public sealed record EnrollmentPlacement(Student Student, RouteEnrollment Enrollment, Guid? StopId, string? StopName,
    int? StopSequence);
