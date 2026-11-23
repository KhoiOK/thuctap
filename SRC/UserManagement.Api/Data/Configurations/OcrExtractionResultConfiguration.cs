using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Api.Entities;

namespace UserManagement.Api.Data.Configurations;

/// <summary>
/// EF Core Fluent configuration for <see cref="OcrExtractionResult"/>. Implements the
/// constraints described in <c>OCR-Cloudinary-Spec.md</c> §6.2.
/// </summary>
public class OcrExtractionResultConfiguration : IEntityTypeConfiguration<OcrExtractionResult>
{
    public void Configure(EntityTypeBuilder<OcrExtractionResult> builder)
    {
        builder.ToTable("OcrExtractionResults");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
               .HasColumnName("Id")
               .HasColumnType("uniqueidentifier");

        builder.Property(o => o.UserId)
               .HasColumnName("UserId")
               .HasColumnType("uniqueidentifier")
               .IsRequired();

        builder.Property(o => o.ScanId)
               .HasColumnName("ScanId")
               .HasColumnType("uniqueidentifier")
               .IsRequired();

        builder.Property(o => o.DocumentType)
               .HasColumnName("DocumentType")
               .HasColumnType("tinyint")
               .HasConversion<byte>()
               .IsRequired();

        builder.Property(o => o.Status)
               .HasColumnName("Status")
               .HasColumnType("tinyint")
               .HasConversion<byte>()
               .IsRequired();

        builder.Property(o => o.FrontImagePublicId)
               .HasColumnName("FrontImagePublicId")
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(o => o.BackImagePublicId)
               .HasColumnName("BackImagePublicId")
               .HasMaxLength(500)
               .IsRequired(false);

        builder.Property(o => o.DocumentId)
               .HasColumnName("DocumentId")
               .HasColumnType("uniqueidentifier")
               .IsRequired(false);

        builder.Property(o => o.RawResult)
               .HasColumnName("RawResult")
               .HasColumnType("nvarchar(max)")
               .IsRequired(false);

        builder.Property(o => o.ExtractedFields)
               .HasColumnName("ExtractedFields")
               .HasColumnType("nvarchar(max)")
               .IsRequired(false);

        builder.Property(o => o.ProcessingTimeMs)
               .HasColumnName("ProcessingTimeMs")
               .HasColumnType("int")
               .IsRequired(false);

        builder.Property(o => o.ErrorMessage)
               .HasColumnName("ErrorMessage")
               .HasMaxLength(1000)
               .IsRequired(false);

        builder.Property(o => o.CreatedAt)
               .HasColumnName("CreatedAt")
               .HasColumnType("datetime2")
               .IsRequired();

        builder.Property(o => o.ExpiresAt)
               .HasColumnName("ExpiresAt")
               .HasColumnType("datetime2")
               .IsRequired(false);

        // ---------- Indexes ----------

        builder.HasIndex(o => o.ScanId)
               .HasDatabaseName("UX_OcrExtractionResults_ScanId")
               .IsUnique();

        builder.HasIndex(o => new { o.UserId, o.CreatedAt })
               .HasDatabaseName("IX_OcrExtractionResults_UserId_CreatedAt");

        builder.HasIndex(o => new { o.Status, o.ExpiresAt })
               .HasDatabaseName("IX_OcrExtractionResults_Status_ExpiresAt");

        // ---------- Relationships ----------

        // NO_ACTION: consistent with IdentityDocuments -> Users. The service deletes the
        // OCR records explicitly when a user is removed.
        builder.HasOne(o => o.User)
               .WithMany()
               .HasForeignKey(o => o.UserId)
               .OnDelete(DeleteBehavior.NoAction)
               .HasConstraintName("FK_OcrExtractionResults_Users");

        // SET NULL: the scan history survives the deletion of the document it produced.
        builder.HasOne(o => o.Document)
               .WithMany()
               .HasForeignKey(o => o.DocumentId)
               .OnDelete(DeleteBehavior.SetNull)
               .HasConstraintName("FK_OcrExtractionResults_IdentityDocuments");
    }
}
