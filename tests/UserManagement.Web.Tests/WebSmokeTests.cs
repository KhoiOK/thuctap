using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UserManagement.Web.Tests;

/// <summary>
/// Smoke tests for the Web project - verifies it boots, redirects "/" -> "/Users",
/// and renders the (placeholder) layout.
/// </summary>
public class WebSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Root_Redirects_To_Users()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/");
        response.StatusCode.Should().BeOneOf(
            (System.Net.HttpStatusCode)Microsoft.AspNetCore.Http.StatusCodes.Status302Found,
            (System.Net.HttpStatusCode)Microsoft.AspNetCore.Http.StatusCodes.Status307TemporaryRedirect);
        response.Headers.Location?.ToString().Should().Be("/Users");
    }
}
