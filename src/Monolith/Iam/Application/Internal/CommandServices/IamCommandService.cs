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

public class IamCommandService(
    IUserRepository userRepository,
    IOrganizationRepository organizationRepository,
    IHashingService hashingService,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : IIamCommandService
{
    public async Task<Result<User>> Handle(SignUpCommand command, CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsByEmailAsync(command.Email, cancellationToken))
            return Result<User>.Failure(IamError.EmailAlreadyRegistered,
                $"Email address {command.Email} is already registered.");

        try
        {
            var organizationId = command.OrganizationId.HasValue
                ? new OrganizationId(command.OrganizationId.Value)
                : null;
            var user = new User(
                organizationId,
                new FullName(command.FirstName, command.LastName),
                new Email(command.Email),
                new PasswordHash(hashingService.HashPassword(command.Password)),
                new RoleTier(command.RoleTier));

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

    public async Task<Result<(User User, string Token)>> Handle(SignInCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByEmailAsync(command.Email, cancellationToken);

        if (user is null || !hashingService.VerifyPassword(command.Password, user.PasswordHash.Value))
            return Result<(User User, string Token)>.Failure(IamError.InvalidCredentials,
                "Invalid email or password.");

        var token = tokenService.GenerateToken(user);
        return Result<(User User, string Token)>.Success((user, token));
    }

    public async Task<Result<Organization>> Handle(CreateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var organization = new Organization(new OrganizationName(command.Name));
            await organizationRepository.AddAsync(organization, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return Result<Organization>.Success(organization);
        }
        catch (ArgumentException ex)
        {
            return Result<Organization>.Failure(IamError.InvalidIamData, ex.Message);
        }
        catch (DbUpdateException)
        {
            return Result<Organization>.Failure(IamError.DatabaseError, "A database error occurred.");
        }
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
}