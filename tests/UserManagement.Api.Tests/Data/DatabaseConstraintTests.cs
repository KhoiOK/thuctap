using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using Xunit;

namespace UserManagement.Api.Tests.Data;

/// <summary>
/// Integration tests for the EF Core data layer. They connect to a real SQL Server instance
/// (LocalDB or full SQL Server) because the unique filtered indexes and global query filter
/// cannot be exercised against an in-memory provider. The test database is created fresh for
/// each test class and dropped on disposal so the suite is self-contained.
/// </summary>
public class DatabaseConstraintTests : IAsyncLifetime
{
    private readonly string _databaseName = $"UserManagementDb_Test_{Guid.NewGuid():N}";
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private ApplicationDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var masterConnectionString =
            "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

        await using (var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(masterConnectionString)
            .Options))
        {
            await setup.Database.ExecuteSqlRawAsync($"CREATE DATABASE [{_databaseName}];");
        }

        var connectionString =
            $"Server=localhost;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .EnableSensitiveDataLogging(false)
            .Options;

        _context = new ApplicationDbContext(_options);
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        var masterConnectionString =
            "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(masterConnectionString)
            .Options);
        await setup.Database.ExecuteSqlRawAsync(
            $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END");
    }

    private static User NewUser(string email, string phone, string name = "Nguyen Van A") => new()
    {
        Id = Guid.NewGuid(),
        FullName = name,
        FullNameSearch = User.BuildFullNameSearch(name),
        Email = email.ToLowerInvariant(),
        PhoneNumber = phone,
        CreatedAt = DateTime.UtcNow,
        IsDeleted = false
    };

    // ---------- BR-01 / UX_Users_Email ----------

    [Fact]
    public async Task Duplicate_Email_On_Active_User_Is_Rejected()
    {
        _context.Users.Add(NewUser("a@example.com", "0912345678"));
        await _context.SaveChangesAsync();

        _context.Users.Add(NewUser("A@example.COM", "0987654321"));
        Func<Task> act = async () => await _context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Email_Can_Be_Reused_After_Soft_Delete()
    {
        var first = NewUser("a@example.com", "0912345678");
        _context.Users.Add(first);
        await _context.SaveChangesAsync();

        first.IsDeleted = true;
        first.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _context.Users.Add(NewUser("a@example.com", "0987654321"));
        await _context.SaveChangesAsync();

        var all = await _context.Users.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
    }

    // ---------- BR-02 / UX_Users_PhoneNumber ----------

    [Fact]
    public async Task Duplicate_PhoneNumber_On_Active_User_Is_Rejected()
    {
        _context.Users.Add(NewUser("a@example.com", "0912345678"));
        await _context.SaveChangesAsync();

        _context.Users.Add(NewUser("b@example.com", "0912345678"));
        Func<Task> act = async () => await _context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------- Global query filter ----------

    [Fact]
    public async Task Global_Query_Filter_Hides_Soft_Deleted_Users()
    {
        var u1 = NewUser("a@example.com", "0912345678");
        var u2 = NewUser("b@example.com", "0987654321");
        _context.Users.AddRange(u1, u2);
        await _context.SaveChangesAsync();

        u1.IsDeleted = true;
        u1.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var visible = await _context.Users.ToListAsync();
        visible.Should().HaveCount(1);
        visible[0].Id.Should().Be(u2.Id);

        var all = await _context.Users.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
    }

    // ---------- BR-06: UX_IdentityDocuments_UserId_DocumentType ----------

    [Fact]
    public async Task User_Cannot_Have_Two_Documents_Of_Same_Type()
    {
        var user = NewUser("a@example.com", "0912345678");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.Add(new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "079095001234",
            FrontImagePublicId = "x.jpg"
        });
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.Add(new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "079095001235",
            FrontImagePublicId = "y.jpg"
        });

        Func<Task> act = async () => await _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ---------- BR-11: UX_IdentityDocuments_Type_Number ----------

    [Fact]
    public async Task Duplicate_Document_Number_Across_Users_Is_Rejected()
    {
        var u1 = NewUser("a@example.com", "0912345678");
        var u2 = NewUser("b@example.com", "0987654321");
        _context.Users.AddRange(u1, u2);
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.Add(new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = u1.Id,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "079095001234",
            FrontImagePublicId = "a.jpg"
        });
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.Add(new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = u2.Id,
            DocumentType = DocumentType.CCCD,
            DocumentNumber = "079095001234",
            FrontImagePublicId = "b.jpg"
        });

        Func<Task> act = async () => await _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Same_Document_Number_Allowed_For_Different_Document_Types()
    {
        var u1 = NewUser("a@example.com", "0912345678");
        _context.Users.Add(u1);
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.AddRange(
            new IdentityDocument
            {
                Id = Guid.NewGuid(),
                UserId = u1.Id,
                DocumentType = DocumentType.CCCD,
                DocumentNumber = "079095001234",
                FrontImagePublicId = "a.jpg"
            },
            new IdentityDocument
            {
                Id = Guid.NewGuid(),
                UserId = u1.Id,
                DocumentType = DocumentType.CMND,
                DocumentNumber = "079095001234",
                FrontImagePublicId = "b.jpg"
            });

        await _context.SaveChangesAsync();
    }

    // ---------- FK delete behavior ----------

    [Fact]
    public async Task Deleting_User_With_Documents_Is_Restricted()
    {
        var user = NewUser("a@example.com", "0912345678");
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.IdentityDocuments.Add(new IdentityDocument
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DocumentType = DocumentType.Passport,
            DocumentNumber = "B1234567",
            FrontImagePublicId = "p.jpg"
        });
        await _context.SaveChangesAsync();

        // Attempt a hard delete directly via SQL so we exercise only the database constraint
        // (the EF Core global query filter otherwise triggers an in-memory cascade check).
        // Expect the FK with ON DELETE NO ACTION to surface as SqlException.
        Func<Task> act = async () => await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM Users WHERE Id = {0}", user.Id);
        await act.Should().ThrowAsync<Exception>();

        // User should still be present.
        var stillThere = await _context.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Id == user.Id);
        stillThere.Should().BeTrue();
    }
}
