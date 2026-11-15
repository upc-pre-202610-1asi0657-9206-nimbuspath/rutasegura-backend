using Monolith.Stakeholder.Domain.Model.ValueObjects;

namespace Monolith.Stakeholder.Domain.Repositories;

/// <summary>
///     Storage of the photo bytes, apart from the aggregate so listing students never loads images. The adapter keeps
///     them in the database (same transaction as the metadata); an object store (S3) can replace it later.
/// </summary>
public interface IStudentPhotoStore
{
    Task SaveAsync(StudentId studentId, string contentType, byte[] content, CancellationToken cancellationToken);

    Task<(string ContentType, byte[] Content)?> FindAsync(StudentId studentId, CancellationToken cancellationToken);

    Task DeleteAsync(StudentId studentId, CancellationToken cancellationToken);
}
