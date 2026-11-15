using Monolith.Shared.Domain.Model.Entities;
using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Model.Aggregates;

/// <summary>
///     Contact data of a parent that the transport company and the driver need (name and e-mail live in Iam).
/// </summary>
public class GuardianProfile : IAuditableEntity
{
    public const int DocumentMaxLength = 12;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected GuardianProfile()
    {
        PhoneNumber = new GuardianPhoneNumber();
    }

    public GuardianProfile(Guid userId, GuardianPhoneNumber phoneNumber, string? documentNumber)
    {
        if (userId == Guid.Empty) throw new ArgumentException("The guardian account is required.", nameof(userId));
        UserId = userId;
        PhoneNumber = phoneNumber ?? throw new ArgumentNullException(nameof(phoneNumber));
        Update(phoneNumber, documentNumber);
    }

    /// <summary>Iam user id of the parent (one profile per account).</summary>
    public Guid UserId { get; private set; }

    public GuardianPhoneNumber PhoneNumber { get; private set; }

    /// <summary>DNI / carné de extranjería (optional): 8 to 12 letters or digits.</summary>
    public string? DocumentNumber { get; private set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public void Update(GuardianPhoneNumber phoneNumber, string? documentNumber)
    {
        PhoneNumber = phoneNumber ?? throw new ArgumentNullException(nameof(phoneNumber));
        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            DocumentNumber = null;
            return;
        }

        var trimmed = documentNumber.Trim().ToUpperInvariant();
        if (trimmed.Length is < 8 or > DocumentMaxLength || !trimmed.All(char.IsLetterOrDigit))
            throw new ArgumentException("The document number must have 8 to 12 letters or digits.",
                nameof(documentNumber));
        DocumentNumber = trimmed;
    }
}
