using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Entities;
using UserManagement.Api.Tests.Infrastructure;
using Xunit;

namespace UserManagement.Api.Tests.Controllers;

/// <summary>
/// End-to-end tests for the User endpoints (API-01..API-05) using
/// <see cref="TestApiFactory"/> so we exercise the full HTTP pipeline including the
/// exception middleware and Problem Details formatter.
/// </summary>
[CollectionDefinition(nameof(ApiIntegrationCollection), DisableParallelization = true)]
public class ApiIntegrationCollection { }

[Collection(nameof(ApiIntegrationCollection))]
public class UsersControllerTests : IAsyncLifetime
{
    private TestApiFactory _factory = null!;
    private HttpClient _client = null!;
    private int _emailCounter;

    public async Task InitializeAsync()
    {
        _factory = new TestApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private async Task<HttpResponseMessage> PostUser(UserUpsertRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOpts);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await _client.PostAsync("/api/v1/users", content);
    }

    private async Task<HttpResponseMessage> PutUser(Guid id, UserUpsertRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOpts);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return await _client.PutAsync($"/api/v1/users/{id}", content);
    }

    private string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";
    private string UniquePhone() => $"09{string.Format("{0:D8}", Random.Shared.Next(0, 100_000_000))}";

    private UserUpsertRequest Sample(string? email = null,
                                    string? phone = null,
                                    string name = "Nguyễn Văn A") => new()
    {
        FullName = name,
        Email = email ?? UniqueEmail(),
        PhoneNumber = phone ?? UniquePhone(),
        DateOfBirth = new DateOnly(1995, 4, 20),
        Gender = Gender.Male,
        Address = "12 Lê Lợi, Q1"
    };

    [Fact]
    public async Task Create_Valid_Returns_201_With_Location_Header()
    {
        var email = UniqueEmail();
        var response = await PostUser(Sample(email: email));
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"body was: {raw}");
        response.Headers.Location.Should().NotBeNull();

        var dto = await response.Content.ReadFromJsonAsync<UserDetailDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Id.Should().NotBe(Guid.Empty);
        dto.Email.Should().Be(email.ToLowerInvariant());
    }

    [Fact]
    public async Task Create_Invalid_Email_Returns_400_With_Errors()
    {
        var response = await PostUser(Sample(email: "not-an-email"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"email\"");
    }

    [Fact]
    public async Task Create_Duplicate_Email_Returns_409()
    {
        var email = UniqueEmail();
        var first = await PostUser(Sample(email: email));
        first.IsSuccessStatusCode.Should().BeTrue();

        var second = await PostUser(Sample(email: email.ToUpperInvariant(), phone: UniquePhone()));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadAsStringAsync();
        body.Should().Contain("\"email\"");
    }

    [Fact]
    public async Task GetById_Unknown_Returns_404()
    {
        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_After_Delete_Returns_404()
    {
        var created = await (await PostUser(Sample())).Content
            .ReadFromJsonAsync<UserDetailDto>(JsonOpts);
        var del = await _client.DeleteAsync($"/api/v1/users/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/users/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Unknown_Returns_404()
    {
        var response = await PutUser(Guid.NewGuid(), Sample());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Duplicate_Email_Returns_409()
    {
        var firstEmail = UniqueEmail();
        await PostUser(Sample(email: firstEmail));
        var second = await (await PostUser(Sample())).Content
            .ReadFromJsonAsync<UserDetailDto>(JsonOpts);

        var response = await PutUser(second!.Id, Sample(email: firstEmail));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_Search_By_Keyword_Returns_Match()
    {
        var common = Guid.NewGuid().ToString("N")[..6];
        await PostUser(Sample(email: $"{common}-nguyen@example.com", name: "Nguyễn Văn An"));
        await PostUser(Sample(email: $"{common}-tran@example.com", name: "Trần Thị Bình"));

        var response = await _client.GetAsync($"/api/v1/users?keyword={common}-nguyen&page=1&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResultDto<UserListItemDto>>(JsonOpts);
        paged!.TotalItems.Should().Be(1);
        paged.Items[0].Email.Should().Be($"{common}-nguyen@example.com");
    }

    [Fact]
    public async Task List_Invalid_SortBy_Returns_400()
    {
        var response = await _client.GetAsync("/api/v1/users?sortBy=hacker");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Unknown_Returns_404()
    {
        var response = await _client.DeleteAsync($"/api/v1/users/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
