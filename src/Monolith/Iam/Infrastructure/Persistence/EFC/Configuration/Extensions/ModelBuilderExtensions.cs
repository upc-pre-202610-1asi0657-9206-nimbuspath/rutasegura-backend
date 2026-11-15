using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace Monolith.Iam.Infrastructure.Persistence.EFC.Configuration.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplyIamConfiguration(this ModelBuilder builder)
    {
        builder.Entity<Organization>(organization =>
        {
            organization.ToTable("Organizations");
            organization.HasKey(o => o.Id);
            organization.Property(o => o.Id).HasConversion(id => id.Identifier, value => new OrganizationId(value))
                .ValueGeneratedNever();
            organization.Property(o => o.Name).HasConversion(name => name.Value, value => new OrganizationName(value))
                .HasMaxLength(OrganizationName.MaxLength).IsRequired();
            organization.Property(o => o.Status)
                .HasConversion(status => status.Value, value => new OrganizationStatus(value))
                .HasMaxLength(20).IsRequired();
        });

        builder.Entity<User>(user =>
        {
            user.ToTable("Users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).HasConversion(id => id.Identifier, value => new UserId(value))
                .ValueGeneratedNever();
            user.Property(u => u.OrganizationId)
                .HasConversion(id => id == null ? (Guid?)null : id.Identifier,
                    value => value == null ? null : new OrganizationId(value.Value));
            user.OwnsOne(u => u.FullName, fullName =>
            {
                fullName.WithOwner().HasForeignKey("Id");
                fullName.Property(f => f.FirstName).HasMaxLength(FullName.MaxLength).IsRequired();
                fullName.Property(f => f.LastName).HasMaxLength(FullName.MaxLength).IsRequired();
            });
            user.Property(u => u.Email).HasConversion(email => email.Value, value => new Email(value))
                .HasMaxLength(Email.MaxLength).IsRequired();
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.PasswordHash)
                .HasConversion(hash => hash.Value, value => new PasswordHash(value))
                .HasMaxLength(255).IsRequired();
            user.Property(u => u.Role).HasConversion(role => role.Value, value => new RoleTier(value))
                .HasMaxLength(20).IsRequired();
        });
    }
}