using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Application.CommandServices;
using Monolith.Iam.Application.Internal.OutboundServices;
using Monolith.Iam.Domain.Model;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.Commands;
using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Iam.Domain.Repositories;
using Monolith.Shared.Application.Model;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Repositories;

namespace Monolith.Iam.Application.Internal.CommandServices;

/// <remarks>
///     Who can create which account:
///     <list type="bullet">
///         <item>PARENT: anyone, from the app (sign-up).</item>
///         <item>ADMIN: exactly one per organization, created together with the organization (provisioning).</item>
///         <item>DRIVER: the administrator of the organization.</item>
///     </list>
/// </remarks>
public class IamCommandService(
    IUserRepository userRepository,
    IOrganizationRepository organizationRepository,
    IHashingService hashingService,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IIamCommandService
{
    public async Task<Result<User>> Handle(SignUpCommand command, CancellationToken cancellationToken)
    {
        OrganizationId? organizationId = null;
        if (command.OrganizationId is { } requested && requested != Guid.Empty)
        {
            var organization = await organizationRepository.FindByOrganizationIdAsync(new OrganizationId(requested),
                cancellationToken);
            if (organization is null)
                return Result<User>.Failure(IamError.OrganizationNotFound, "Organization was not found.");
            if (!organization.IsActive())
                return Result<User>.Failure(IamError.OrganizationNotActive, "The organization is not active.");
            organizationId = organization.Id;
        }

        return await CreateAccountAsync(organizationId, command.FirstName, command.LastName, command.Email,
            command.Password, RoleTier.Parent, cancellationToken);
    }

    public async Task<Result<(User User, string Token)>> Handle(SignInCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByEmailAsync(command.Email, cancellationToken);

        if (user is null || !hashingService.VerifyPassword(command.Password, user.PasswordHash.Value))
            return Result<(User User, string Token)>.Failure(IamError.InvalidCredentials,
                "Invalid email or password.");

        if (user.OrganizationId is not null)
        {
            var organization = await organizationRepository.FindByOrganizationIdAsync(user.OrganizationId,
                cancellationToken);
            if (organization is null || !organization.IsActive())
                return Result<(User User, string Token)>.Failure(IamError.OrganizationNotActive,
                    "The organization of the account is not active.");
        }

        var token = tokenService.GenerateToken(user);
        return Result<(User User, string Token)>.Success((user, token));
    }

    public async Task<Result<(Organization Organization, User Administrator)>> Handle(
        ProvisionOrganizationCommand command, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsSatisfiedBy(command.AdministratorPassword))
            return Result<(Organization, User)>.Failure(IamError.WeakPassword, PasswordPolicy.Description);
        if (await userRepository.ExistsByEmailAsync(command.AdministratorEmail, cancellationToken))
            return Result<(Organization, User)>.Failure(IamError.EmailAlreadyRegistered,
                $"Email address {command.AdministratorEmail} is already registered.");

        try
        {
            var organization = new Organization(new OrganizationName(command.OrganizationName));
            var administrator = new User(
                organization.Id,
                new FullName(command.AdministratorFirstName, command.AdministratorLastName),
                new Email(command.AdministratorEmail),
                new PasswordHash(hashingService.HashPassword(command.AdministratorPassword)),
                new RoleTier(RoleTier.Admin));

            // Organization and administrator are committed together: no organization without its administrator.
            await organizationRepository.AddAsync(organization, cancellationToken);
            await userRepository.AddAsync(administrator, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<(Organization, User)>.Success((organization, administrator));
        }
        catch (ArgumentException ex)
        {
            return Result<(Organization, User)>.Failure(IamError.InvalidIamData, ex.Message);
        }
        catch (DbUpdateException)
        {
            return Result<(Organization, User)>.Failure(IamError.DatabaseError, "A database error occurred.");
        }
    }

    public async Task<Result<User>> Handle(RegisterDriverAccountCommand command, CancellationToken cancellationToken)
    {
        if (command.OrganizationId == Guid.Empty)
            return Result<User>.Failure(IamError.InvalidIamData, "The organization of the administrator is required.");
        var organization = await organizationRepository.FindByOrganizationIdAsync(
            new OrganizationId(command.OrganizationId), cancellationToken);
        if (organization is null)
            return Result<User>.Failure(IamError.OrganizationNotFound, "Organization was not found.");
        if (!organization.IsActive())
            return Result<User>.Failure(IamError.OrganizationNotActive, "The organization is not active.");

        return await CreateAccountAsync(organization.Id, command.FirstName, command.LastName, command.Email,
            command.Password, RoleTier.Driver, cancellationToken);
    }

    public async Task<Result<Organization>> Handle(UpdateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var organization =
            await organizationRepository.FindByOrganizationIdAsync(new OrganizationId(command.OrganizationId),
                cancellationToken);
        if (organization is null)
            return Result<Organization>.Failure(IamError.OrganizationNotFound, "Organization was not found.");

        try
        {
            organization.UpdateName(new OrganizationName(command.Name));
            organizationRepository.Update(organization);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Organization>.Success(organization);
        }
        catch (ArgumentException ex)
        {
            return Result<Organization>.Failure(IamError.InvalidIamData, ex.Message);
        }
    }

    private async Task<Result<User>> CreateAccountAsync(OrganizationId? organizationId, string firstName,
        string lastName, string email, string password, string role, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsSatisfiedBy(password))
            return Result<User>.Failure(IamError.WeakPassword, PasswordPolicy.Description);
        if (await userRepository.ExistsByEmailAsync(email, cancellationToken))
            return Result<User>.Failure(IamError.EmailAlreadyRegistered,
                $"Email address {email} is already registered.");

        try
        {
            var user = new User(
                organizationId,
                new FullName(firstName, lastName),
                new Email(email),
                new PasswordHash(hashingService.HashPassword(password)),
                new RoleTier(role));

            await userRepository.AddAsync(user, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<User>.Success(user);
        }
        catch (ArgumentException ex)
        {
            return Result<User>.Failure(IamError.InvalidIamData, ex.Message);
        }
        catch (DbUpdateException)
        {
            return Result<User>.Failure(IamError.DatabaseError, "A database error occurred.");
        }
    }
}
