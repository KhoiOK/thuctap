using Microsoft.EntityFrameworkCore;
using UserManagement.Api.Entities;

namespace UserManagement.Api.Data;

/// <summary>
/// Application DbContext. Uses Code-First with Fluent API configurations located in
/// <see cref="Configurations"/>. Schema is created/updated via EF Core Migrations.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<IdentityDocument> IdentityDocuments => Set<IdentityDocument>();
    public DbSet<OcrExtractionResult> OcrExtractionResults => Set<OcrExtractionResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
