using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Api.Entities;

namespace UserManagement.Api.Data.Configurations;

/// <summary>
/// EF Core Fluent configuration for <see cref="User"/>. Implements the constraints
/// described in <c>Database-Design.md</c> §2 and §5.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
               .HasColumnName("Id")
               .HasColumnType("uniqueidentifier");

        builder.Property(u => u.FullName)
               .HasColumnName("FullName")
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(u => u.FullNameSearch)
               .HasColumnName("FullNameSearch")
               .HasMaxLength(100)
               .IsRequired();

        builder.Property(u => u.Email)
               .HasColumnName("Email")
               .HasMaxLength(255)
               .IsRequired();

        builder.Property(u => u.PhoneNumber)
               .HasColumnName("PhoneNumber")
               .HasMaxLength(15)
               .IsRequired();

        builder.Property(u => u.DateOfBirth)
               .HasColumnName("DateOfBirth")
               .HasColumnType("date");

        builder.Property(u => u.Gender)
               .HasColumnName("Gender")
               .HasColumnType("tinyint")
               .IsRequired(false);

        builder.Property(u => u.Address)
               .HasColumnName("Address")
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(u => u.IsDeleted)
               .HasColumnName("IsDeleted")
               .HasColumnType("bit")
               .HasDefaultValue(false)
               .IsRequired();

        builder.Property(u => u.DeletedAt)
               .HasColumnName("DeletedAt")
               .HasColumnType("datetime2")
               .IsRequired(false);

        builder.Property(u => u.CreatedAt)
               .HasColumnName("CreatedAt")
               .HasColumnType("datetime2")
               .IsRequired();

        builder.Property(u => u.UpdatedAt)
               .HasColumnName("UpdatedAt")
               .HasColumnType("datetime2")
               .IsRequired(false);

        // ---------- Indexes ----------

        // UX_Users_Email - unique filtered index for non-deleted users only.
        builder.HasIndex(u => u.Email)
               .HasDatabaseName("UX_Users_Email")
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        // UX_Users_PhoneNumber - unique filtered index for non-deleted users only.
        builder.HasIndex(u => u.PhoneNumber)
               .HasDatabaseName("UX_Users_PhoneNumber")
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        // IX_Users_FullName - non-clustered filtered index for sorting by full name.
        builder.HasIndex(u => new { u.FullName, u.Id })
               .HasDatabaseName("IX_Users_FullName")
               .HasFilter("[IsDeleted] = 0");

        // IX_Users_CreatedAt - non-clustered filtered index for default sort.
        builder.HasIndex(u => new { u.CreatedAt, u.Id })
               .HasDatabaseName("IX_Users_CreatedAt")
               .HasFilter("[IsDeleted] = 0");

        // ---------- Global query filter ----------
        // All normal queries skip soft-deleted users.
        builder.HasQueryFilter(u => !u.IsDeleted);

        // ---------- Relations ----------
        builder.HasMany(u => u.IdentityDocuments)
               .WithOne(d => d.User!)
               .HasForeignKey(d => d.UserId)
               .OnDelete(DeleteBehavior.Restrict)
               .HasConstraintName("FK_IdentityDocuments_Users");
    }
}
