using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Monolith.Shared.Domain.Model.Entities;

namespace Monolith.Shared.Infrastructure.Persistence.EFC.Interceptors;
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
   /// <inheritdoc />
   public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
   {
       ApplyAuditTimestamps(eventData.Context);
       return base.SavingChanges(eventData, result);
   }


   /// <inheritdoc />
   public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
       DbContextEventData eventData,
       InterceptionResult<int> result,
       CancellationToken cancellationToken = default)
   {
       ApplyAuditTimestamps(eventData.Context);
       return base.SavingChangesAsync(eventData, result, cancellationToken);
   }


   private static void ApplyAuditTimestamps(DbContext? context)
   {
       if (context is null) return;


       var now = DateTimeOffset.UtcNow;


       foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
       {
           if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
           if (entry.State == EntityState.Added) entry.Entity.CreatedAt ??= now;
       }
   }
}