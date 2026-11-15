using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Repositories;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Application.IntegrationEvents;
using Monolith.Stakeholder.Domain.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Commands;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;
namespace Monolith.Stakeholder.Application.Internal.CommandServices;
public sealed class GuardianAccessCommandService(IGuardianAccessRepository grants, IStudentRepository students,
    IStakeholderCommandService commands, IGuardianDirectory directory, IStakeholderEventOutbox outbox, IUnitOfWork work, TimeProvider clock) : IGuardianAccessCommandService
{
    public async Task<Result<OrganizationGuardian>> RegisterGuardianAsync(Guid organization, Guid guardian, string phone, string? document, CancellationToken ct)
    {
        if (organization == Guid.Empty || !await directory.IsOrganizationActiveAsync(organization, ct) || (await directory.FindAccountAsync(guardian, ct))?.Role != "PARENT")
            return Result<OrganizationGuardian>.Failure(StakeholderError.StudentNotFound, "Guardian account was not found.");
        var existing = await grants.FindGuardianAsync(organization, guardian, ct);
        if (existing is not null) return Result<OrganizationGuardian>.Success(existing);
        try
        {
            var member = new OrganizationGuardian(organization, guardian, phone, document);
            grants.AddGuardian(member); await work.CompleteAsync(ct); return Result<OrganizationGuardian>.Success(member);
        }
        catch (ArgumentException) { return Result<OrganizationGuardian>.Failure(StakeholderError.InvalidStakeholderData, "Invalid guardian contact data."); }
    }
    public async Task<Result<Student>> RegisterStudentAsync(Guid organization, RegisterStudentCommand command, CancellationToken ct)
    {
        if (await grants.FindGuardianAsync(organization, command.GuardianUserId, ct) is null)
            return Result<Student>.Failure(StakeholderError.StudentNotFound, "Register the guardian in this operation first.");
        await using var transaction = await work.BeginTransactionAsync(ct);
        var result = await commands.Handle(command, ct);
        if (result.IsFailure) return result;
        grants.Add(new GuardianAccessGrant(organization, command.GuardianUserId, result.Value!.Id.Identifier, clock.GetUtcNow()));
        await work.CompleteAsync(ct); await transaction.CommitAsync(ct); return result;
    }
    public async Task<Result<GuardianAccessGrant>> ReviewAsync(Guid organization, Guid actor, Guid guardian, Guid studentId, bool verify, string reference, CancellationToken ct)
    {
        var student = await students.FindByStudentIdAsync(new StudentId(studentId), ct);
        var grant = await grants.FindAsync(organization, guardian, studentId, ct);
        // Scope must already exist through enrollment or administrative registration, never a free student id.
        if (student is null || student.IsRemoved() || student.GuardianUserId != guardian || grant is null ||
            !await directory.IsOrganizationActiveAsync(organization, ct) || (await directory.FindAccountAsync(guardian, ct))?.Role != "PARENT")
            return Result<GuardianAccessGrant>.Failure(StakeholderError.StudentNotFound, "Guardian/student was not found in this operation.");
        try
        {
            await using var transaction = await work.BeginTransactionAsync(ct);
            grant.Review(verify, actor, reference, clock.GetUtcNow());
            await outbox.EnqueueAsync(new StakeholderIntegrationEvent(Guid.NewGuid(), verify ? StakeholderMessageTypes.GuardianStudentLinked : StakeholderMessageTypes.GuardianStudentUnlinked,
                1, clock.GetUtcNow(), new GuardianStudentLinkV1(guardian, studentId, organization, grant.Version)), ct);
            await work.CompleteAsync(ct); await transaction.CommitAsync(ct);
            return Result<GuardianAccessGrant>.Success(grant);
        }
        catch (ArgumentException) { return Result<GuardianAccessGrant>.Failure(StakeholderError.InvalidStakeholderData, "A verification reference or revocation reason is required (1-300 characters)."); }
    }
}
