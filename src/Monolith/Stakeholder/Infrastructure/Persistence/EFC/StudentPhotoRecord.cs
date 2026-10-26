namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC;

/// <summary>Bytes of a student's photo (one row per student), apart from the aggregate.</summary>
public sealed class StudentPhotoRecord
{
    public Guid StudentId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}
