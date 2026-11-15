namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

/// <summary>
///     Metadata of the child's photo (the bytes are stored apart, see <c>IStudentPhotoStore</c>). Only JPEG, PNG
///     and WebP up to 1 MB are accepted; the format is checked on the content itself, not on the file name.
/// </summary>
public record StudentPhoto
{
    public const int MaxSizeBytes = 1_048_576;
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public StudentPhoto()
    {
        ContentType = string.Empty;
        Version = string.Empty;
    }

    public StudentPhoto(string contentType, int sizeBytes, DateTimeOffset uploadedAt)
    {
        if (contentType is not (Jpeg or Png or WebP))
            throw new ArgumentException("The photo must be a JPEG, PNG or WebP image.", nameof(contentType));
        if (sizeBytes is <= 0 or > MaxSizeBytes)
            throw new ArgumentException($"The photo must be at most {MaxSizeBytes / 1024} KB.", nameof(sizeBytes));
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAt = uploadedAt;
        Version = Guid.NewGuid().ToString("N")[..12];
    }

    public string ContentType { get; init; }

    public int SizeBytes { get; init; }

    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Changes with every upload (ETag / cache busting).</summary>
    public string Version { get; init; }

    /// <summary>Detects the image format from its first bytes; null when it is not an accepted format.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return Jpeg;
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return Png;
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
            return WebP;
        return null;
    }
}
