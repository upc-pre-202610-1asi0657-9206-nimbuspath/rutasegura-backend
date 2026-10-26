using Monolith.Iam.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Monolith.Administration.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Monolith.Stakeholder.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;
using Monolith.Shared.Infrastructure.Persistence.EFC.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;


public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        // Apply audit timestamp interceptor for all IAuditableEntity implementations
        builder.AddInterceptors(new AuditableEntityInterceptor());
        base.OnConfiguring(builder);
    }


    /// <summary>
    ///     On creating the database model
    /// </summary>
    /// <remarks>
    ///     This method is used to create the database model for the application.
    /// </remarks>
    /// <param name="builder">
    ///     The model builder for the database context
    /// </param>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Per-bounded-context model configuration (declared in each context's Infrastructure layer).
        builder.ApplyIamConfiguration();
        builder.ApplyAdministrationConfiguration();
        builder.ApplyStakeholderConfiguration();
        // General Naming Convention for the database objects
        builder.UseSnakeCaseNamingConvention();
    }
}
