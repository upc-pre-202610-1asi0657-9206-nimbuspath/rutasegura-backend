using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Iam.Domain.Model.Aggregates;
public class User
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected User()
    {
        Id = new UserId();
        OrganizationId = null;
        FullName = new FullName();
        Email = new Email();
        PasswordHash = new PasswordHash();
        Role = new RoleTier();
    }

    /// <summary>Creates a new user account with an already-hashed password.</summary>
    public User(OrganizationId? organizationId, FullName fullName, Email email, PasswordHash passwordHash,
        RoleTier role)
    {
        Id = UserId.New();
        OrganizationId = organizationId;
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    public UserId Id { get; private set; }

    public OrganizationId? OrganizationId { get; private set; }

    public FullName FullName { get; private set; }

    public Email Email { get; private set; }

    public PasswordHash PasswordHash { get; private set; }

    public RoleTier Role { get; private set; }

    public bool IsAdmin() => Role.IsAdmin();

    public string GetFullName() => FullName.ToString();

    public string GetEmail() => Email.ToString();
}