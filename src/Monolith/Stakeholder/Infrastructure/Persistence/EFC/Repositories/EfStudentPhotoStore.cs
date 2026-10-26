using Microsoft.EntityFrameworkCore;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Stakeholder.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Repositories;

namespace Monolith.Stakeholder.Infrastructure.Persistence.EFC.Repositories;

/// <summary>Keeps the photo bytes in MySQL; the change is saved by the same unit of work as the metadata.</summary>
public class EfStudentPhotoStore(AppDbContext context, TimeProvider clock) : IStudentPhotoStore
{
    public async Task SaveAsync(StudentId studentId, string contentType, byte[] content,
        CancellationToken cancellationToken)
    {
        var record = await context.Set<StudentPhotoRecord>()
            .FirstOrDefaultAsync(p => p.StudentId == studentId.Identifier, cancellationToken);
        if (record is null)
        {
            record = new StudentPhotoRecord { StudentId = studentId.Identifier };
            context.Set<StudentPhotoRecord>().Add(record);
        }

        record.ContentType = contentType;
        record.Content = content;
        record.UpdatedAt = clock.GetUtcNow();
    }

    public async Task<(string ContentType, byte[] Content)?> FindAsync(StudentId studentId,
        CancellationToken cancellationToken)
    {
        var record = await context.Set<StudentPhotoRecord>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.StudentId == studentId.Identifier, cancellationToken);
        return record is null ? null : (record.ContentType, record.Content);
    }

    public async Task DeleteAsync(StudentId studentId, CancellationToken cancellationToken)
    {
        var record = await context.Set<StudentPhotoRecord>()
            .FirstOrDefaultAsync(p => p.StudentId == studentId.Identifier, cancellationToken);
        if (record is not null) context.Set<StudentPhotoRecord>().Remove(record);
    }
}
