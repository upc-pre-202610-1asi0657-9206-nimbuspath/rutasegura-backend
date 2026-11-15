namespace Monolith.Stakeholder.Domain.Model.Aggregates;
/// <summary>Administrative authorization for one guardian/student/operation; enrollment does not grant access.</summary>
public sealed class GuardianAccessGrant
{
    private GuardianAccessGrant() { }
    public GuardianAccessGrant(Guid organizationId, Guid guardianUserId, Guid studentId, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || guardianUserId == Guid.Empty || studentId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        OrganizationId = organizationId; GuardianUserId = guardianUserId; StudentId = studentId; RequestedAt = now;
    }
    public Guid OrganizationId { get; private set; }
    public Guid GuardianUserId { get; private set; }
    public Guid StudentId { get; private set; }
    public string Status { get; private set; } = "PENDING";
    public string? VerificationReference { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public long Version { get; private set; }
    public void Review(bool verify, Guid actor, string reference, DateTimeOffset now)
    {
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 300) throw new ArgumentException("A reviewer and verification reference/revocation reason (1-300 characters) are required.");
        Status = verify ? "VERIFIED" : "REVOKED"; ReviewedBy = actor; VerificationReference = reference.Trim(); ReviewedAt = now; Version++;
    }
}
public sealed class OrganizationGuardian
{
    private OrganizationGuardian() { PhoneNumber = ""; }
    public OrganizationGuardian(Guid organizationId, Guid userId, string phoneNumber, string? documentNumber)
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        OrganizationId = organizationId; UserId = userId;
        PhoneNumber = new ValueObjects.GuardianPhoneNumber(phoneNumber).Value;
        if (documentNumber?.Length > 30) throw new ArgumentException("Document number is too long.");
        DocumentNumber = documentNumber;
    }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string PhoneNumber { get; private set; }
    public string? DocumentNumber { get; private set; }
}
