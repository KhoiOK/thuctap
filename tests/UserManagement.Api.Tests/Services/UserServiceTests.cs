using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Services;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Api.Tests.Services;

/// <summary>
/// Integration tests for <see cref="UserService"/>. They use a real SQL Server database
/// created per test class so that global query filters and unique indexes are exercised
/// exactly as in production.
/// </summary>
public class UserServiceTests : IAsyncLifetime
{
    private readonly string _databaseName = $"UserManagementDb_Test_{Guid.NewGuid():N}";
    private ApplicationDbContext _db = null!;
    private UserService _sut = null!;
    private TestCloudinaryStorage _cloudinary = null!;

    public async Task InitializeAsync()
    {
        var master = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using (var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(master).Options))
        {
            await setup.Database.ExecuteSqlRawAsync($"CREATE DATABASE [{_databaseName}];");
        }

        var connection = $"Server=localhost;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;
        _db = new ApplicationDbContext(options);
        await _db.Database.MigrateAsync();

        _cloudinary = new TestCloudinaryStorage();

        _sut = new UserService(_db, _cloudinary, NullLogger<UserService>.Instance, TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        var master = "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(master).Options);
        await setup.Database.ExecuteSqlRawAsync(
            $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END");

    }

    private static UserUpsertRequest SampleRequest(string email = "a.nguyen@example.com",
                                                   string phone = "0912345678",
                                                   string name = "Nguyễn Văn A") => new()
    {
        FullName = name,
        Email = email,
        PhoneNumber = phone,
        DateOfBirth = new DateOnly(1995, 4, 20),
        Gender = Gender.Male,
        Address = "12 Lê Lợi, Q1"
    };

    // ---------- API-03 Create ----------

    [Fact]
    public async Task Create_Persists_Normalized_Data()
    {
        var result = await _sut.CreateAsync(
            SampleRequest(email: "  A.Nguyen@Example.COM ", phone: "+84 912 345 678", name: "  Nguyễn Văn A  "),
            CancellationToken.None);

        result.Email.Should().Be("a.nguyen@example.com");
        result.PhoneNumber.Should().Be("0912345678");
        result.FullName.Should().Be("Nguyễn Văn A");
        result.IdentityDocuments.Should().BeEmpty();

        var fromDb = await _db.Users.IgnoreQueryFilters().FirstAsync();
        fromDb.FullNameSearch.Should().Be("nguyen van a"); // no diacritics
    }

    [Fact]
    public async Task Create_Duplicate_Email_Throws_Conflict()
    {
        await _sut.CreateAsync(SampleRequest(email: "dup@example.com"), CancellationToken.None);
        Func<Task> act = async () => await _sut.CreateAsync(
            SampleRequest(email: "DUP@example.com", phone: "0987654321"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
                 .Where(e => e.Field == "email");
    }

    [Fact]
    public async Task Create_Duplicate_Phone_Throws_Conflict()
    {
        await _sut.CreateAsync(SampleRequest(phone: "0912345678"), CancellationToken.None);
        Func<Task> act = async () => await _sut.CreateAsync(
            SampleRequest(email: "other@example.com", phone: "0912 345 678"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
                 .Where(e => e.Field == "phoneNumber");
    }

    [Fact]
    public async Task Create_Invalid_Email_Throws_Validation()
    {
        Func<Task> act = async () => await _sut.CreateAsync(
            SampleRequest(email: "not-an-email"), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("email"));
    }

    [Fact]
    public async Task Create_Invalid_Phone_Throws_Validation()
    {
        Func<Task> act = async () => await _sut.CreateAsync(
            SampleRequest(phone: "12345"), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("phoneNumber"));
    }

    [Fact]
    public async Task Create_Future_DateOfBirth_Throws_Validation()
    {
        Func<Task> act = async () => await _sut.CreateAsync(new UserUpsertRequest
        {
            FullName = "Test",
            Email = "a@b.com",
            PhoneNumber = "0912345678",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        }, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("dateOfBirth"));
    }

    [Fact]
    public async Task Create_Short_FullName_Throws_Validation()
    {
        Func<Task> act = async () => await _sut.CreateAsync(
            SampleRequest(name: "A"), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("fullName"));
    }

    // ---------- API-02 GetById ----------

    [Fact]
    public async Task GetById_Returns_NotFound_For_Unknown()
    {
        Func<Task> act = async () => await _sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------- API-04 Update ----------

    [Fact]
    public async Task Update_Changes_Fields_And_Touches_UpdatedAt()
    {
        var created = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);

        var updated = await _sut.UpdateAsync(created.Id, new UserUpsertRequest
        {
            FullName = "Nguyễn Văn B",
            Email = "b.nguyen@example.com",
            PhoneNumber = "0987654321",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = Gender.Female,
            Address = null // optional becomes null
        }, CancellationToken.None);

        updated.FullName.Should().Be("Nguyễn Văn B");
        updated.Email.Should().Be("b.nguyen@example.com");
        updated.Address.Should().BeNull();
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_Allows_Same_Email_For_Same_User()
    {
        var created = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);
        var result = await _sut.UpdateAsync(created.Id, SampleRequest(), CancellationToken.None);
        result.Email.Should().Be(created.Email);
    }

    [Fact]
    public async Task Update_Duplicate_Email_From_Other_User_Throws_Conflict()
    {
        await _sut.CreateAsync(SampleRequest(email: "a@example.com"), CancellationToken.None);
        var second = await _sut.CreateAsync(SampleRequest(email: "b@example.com", phone: "0987654321"), CancellationToken.None);

        Func<Task> act = async () => await _sut.UpdateAsync(second.Id,
            SampleRequest(email: "a@example.com", phone: "0987654321"), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    // ---------- API-05 Delete ----------

    [Fact]
    public async Task Delete_Soft_Deletes_And_Hides_From_Default_Query()
    {
        var created = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);
        var ok = await _sut.SoftDeleteAsync(created.Id, CancellationToken.None);
        ok.Should().BeTrue();

        Func<Task> act = async () => await _sut.GetByIdAsync(created.Id, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_Allows_Email_Reuse()
    {
        var first = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);
        await _sut.SoftDeleteAsync(first.Id, CancellationToken.None);

        // Reusing the same email after delete must succeed.
        var second = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);
        second.Email.Should().Be("a.nguyen@example.com");
    }

    [Fact]
    public async Task Delete_Twice_Returns_False()
    {
        var created = await _sut.CreateAsync(SampleRequest(), CancellationToken.None);
        (await _sut.SoftDeleteAsync(created.Id, CancellationToken.None)).Should().BeTrue();
        (await _sut.SoftDeleteAsync(created.Id, CancellationToken.None)).Should().BeFalse();
    }

    // ---------- API-01 List ----------

    [Fact]
    public async Task List_Returns_Empty_With_Correct_Shape()
    {
        var page = await _sut.GetPagedAsync(new UserListQuery(), CancellationToken.None);
        page.Items.Should().BeEmpty();
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(10);
        page.TotalItems.Should().Be(0);
        page.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task List_Paginates_And_Sorts_By_CreatedAt_Desc()
    {
        for (var i = 0; i < 25; i++)
        {
            await _sut.CreateAsync(SampleRequest(
                email: $"user{i:D2}@example.com",
                phone: $"0912345{i:D3}",
                name: $"User {i:D2}"), CancellationToken.None);
        }

        var firstPage = await _sut.GetPagedAsync(new UserListQuery { Page = 1, PageSize = 10 }, CancellationToken.None);
        firstPage.Items.Should().HaveCount(10);
        firstPage.TotalItems.Should().Be(25);
        firstPage.TotalPages.Should().Be(3);
        firstPage.Items[0].Email.Should().Be("user24@example.com");

        var secondPage = await _sut.GetPagedAsync(new UserListQuery { Page = 2, PageSize = 10 }, CancellationToken.None);
        secondPage.Items[0].Email.Should().Be("user14@example.com");
    }

    [Fact]
    public async Task List_Search_By_Name_Is_Diacritic_Insensitive()
    {
        await _sut.CreateAsync(SampleRequest(email: "nguyen@example.com", phone: "0911111111", name: "Nguyễn Văn An"), CancellationToken.None);
        await _sut.CreateAsync(SampleRequest(email: "tran@example.com", phone: "0922222222", name: "Trần Thị Bình"), CancellationToken.None);

        var result = await _sut.GetPagedAsync(new UserListQuery { Keyword = "nguyen" }, CancellationToken.None);
        result.TotalItems.Should().Be(1);
        result.Items[0].Email.Should().Be("nguyen@example.com");
    }

    [Fact]
    public async Task List_Invalid_SortBy_Returns_400()
    {
        Func<Task> act = async () => await _sut.GetPagedAsync(new UserListQuery { SortBy = "hacker" }, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
                 .Where(e => e.Errors.ContainsKey("sortBy"));
    }

    [Fact]
    public async Task List_Caps_PageSize_To_100()
    {
        var result = await _sut.GetPagedAsync(new UserListQuery { PageSize = 9999 }, CancellationToken.None);
        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task List_Hides_Soft_Deleted_Users()
    {
        var u1 = await _sut.CreateAsync(SampleRequest(email: "alive@example.com", phone: "0911111111"), CancellationToken.None);
        var u2 = await _sut.CreateAsync(SampleRequest(email: "gone@example.com", phone: "0922222222"), CancellationToken.None);
        await _sut.SoftDeleteAsync(u2.Id, CancellationToken.None);

        var page = await _sut.GetPagedAsync(new UserListQuery(), CancellationToken.None);
        page.Items.Should().ContainSingle(x => x.Id == u1.Id);
        page.Items.Should().NotContain(x => x.Id == u2.Id);
    }
}
