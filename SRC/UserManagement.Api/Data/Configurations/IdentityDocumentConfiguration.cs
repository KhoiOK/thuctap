using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Api.Entities;

namespace UserManagement.Api.Data.Configurations;

/// <summary>
/// EF Core Fluent configuration for <see cref="IdentityDocument"/>. Implements the
/// constraints described in <c>Database-Design.md</c> §3 and §5, updated by
/// <c>OCR-Cloudinary-Spec.md</c> §6.1 (image columns now hold Cloudinary public ids).
/// </summary>
public class IdentityDocumentConfiguration : IEntityTypeConfiguration<IdentityDocument>
{
    public void Configure(EntityTypeBuilder<IdentityDocument> builder)
    {
        builder.ToTable("IdentityDocuments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
               .HasColumnName("Id")
               .HasColumnType("uniqueidentifier");

        builder.Property(d => d.UserId)
               .HasColumnName("UserId")
               .HasColumnType("uniqueidentifier")
               .IsRequired();

        builder.Property(d => d.DocumentType)
               .HasColumnName("DocumentType")
               .HasColumnType("tinyint")
               .HasConversion<byte>()
               .IsRequired();

        builder.Property(d => d.DocumentNumber)
               .HasColumnName("DocumentNumber")
               .HasMaxLength(20)
               .IsRequired();

        builder.Property(d => d.IssuedDate)
               .HasColumnName("IssuedDate")
               .HasColumnType("date")
               .IsRequired(false);

        builder.Property(d => d.ExpiryDate)
               .HasColumnName("ExpiryDate")
               .HasColumnType("date")
               .IsRequired(false);

        builder.Property(d => d.FrontImagePublicId)
               .HasColumnName("FrontImagePublicId")
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(d => d.BackImagePublicId)
               .HasColumnName("BackImagePublicId")
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(d => d.FrontImageVersion)
               .HasColumnName("FrontImageVersion")
               .HasColumnType("bigint")
               .IsRequired(false);

        builder.Property(d => d.BackImageVersion)
               .HasColumnName("BackImageVersion")
               .HasColumnType("bigint")
               .IsRequired(false);

        builder.Property(d => d.FrontImageWidth)
               .HasColumnName("FrontImageWidth")
               .HasColumnType("int")
               .IsRequired(false);

        builder.Property(d => d.FrontImageHeight)
               .HasColumnName("FrontImageHeight")
               .HasColumnType("int")
               .IsRequired(false);

        builder.Property(d => d.BackImageWidth)
               .HasColumnName("BackImageWidth")
               .HasColumnType("int")
               .IsRequired(false);

        builder.Property(d => d.BackImageHeight)
               .HasColumnName("BackImageHeight")
               .HasColumnType("int")
               .IsRequired(false);

        builder.Property(d => d.CreatedAt)
               .HasColumnName("CreatedAt")
               .HasColumnType("datetime2")
               .IsRequired();

        builder.Property(d => d.UpdatedAt)
               .HasColumnName("UpdatedAt")
               .HasColumnType("datetime2")
               .IsRequired(false);

        // ---------- Indexes ----------

        // UX_IdentityDocuments_UserId_DocumentType - one document of each type per user.
        builder.HasIndex(d => new { d.UserId, d.DocumentType })
               .HasDatabaseName("UX_IdentityDocuments_UserId_DocumentType")
               .IsUnique();

        // UX_IdentityDocuments_Type_Number - uniqueness of (DocumentType, DocumentNumber) globally.
        builder.HasIndex(d => new { d.DocumentType, d.DocumentNumber })
               .HasDatabaseName("UX_IdentityDocuments_Type_Number")
               .IsUnique();
    }
}
