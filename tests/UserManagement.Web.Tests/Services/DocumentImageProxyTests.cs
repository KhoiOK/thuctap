using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using UserManagement.Web.Controllers;
using UserManagement.Web.Services;
using Xunit;

namespace UserManagement.Web.Tests.Services;

/// <summary>
/// Tests for the image proxy of the MVC layer (<c>UsersApiClient.GetDocumentImageAsync</c>
/// and <c>DocumentsController.Image</c>).
///
/// The API answers a conditional request with <c>304 Not Modified</c>. That status is
/// NOT a success status in <see cref="HttpResponseMessage"/> (<c>IsSuccessStatusCode</c>
/// only covers 2xx), so treating it as an error made the proxy answer 404 instead of
/// 304 and dropped the caching validators (ETag / Last-Modified / Cache-Control) that
/// the browser needs to revalidate its cache.
/// </summary>
public class DocumentImageProxyTests
{
    /// <summary>
    /// A stub <see cref="IApiClient"/> whose handler answers with a canned response.
    /// </summary>
    private sealed class StubApiHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubApiHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static UsersApiClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        return new UsersApiClient(new ApiClient(http), NullLogger<UsersApiClient>.Instance);
    }

    /// <summary>
    /// The ETag the API computes for an identity document image
    /// (<c>W/"&lt;size&gt;-&lt;version&gt;-&lt;hash&gt;"</c>).
    /// </summary>
    private const string ETagTag = "\"100-1-abcdef0123456789\"";

    private static EntityTagHeaderValue BuildETag() => new(ETagTag, isWeak: true);

    /// <summary>
    /// Builds a 304 response with the caching validators the API sends.
    /// </summary>
    private static HttpResponseMessage BuildNotModified()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        response.Headers.ETag = BuildETag();
        response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, Private = true };
        return response;
    }

    private static HttpResponseMessage BuildOk(byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent(body);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        response.Headers.ETag = BuildETag();
        response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, Private = true };
        response.Content.Headers.LastModified = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        return response;
    }

    [Fact]
    public async Task GetDocumentImageAsync_Returns_304_And_Keeps_The_Caching_Headers()
    {
        var handler = new StubApiHandler(_ => BuildNotModified());
        var client = CreateClient(handler);

        var response = await client.GetDocumentImageAsync(
            Guid.NewGuid(), Guid.NewGuid(), "front", BuildETag());

        response.Should().NotBeNull("304 is a valid answer for a conditional request");
        response!.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // The validators must survive so the controller can hand them to the browser.
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.ToString().Should().Contain("abcdef0123456789");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task GetDocumentImageAsync_Forwards_The_If_None_Match_Header()
    {
        var handler = new StubApiHandler(_ => BuildNotModified());
        var client = CreateClient(handler);

        var etag = BuildETag();
        await client.GetDocumentImageAsync(Guid.NewGuid(), Guid.NewGuid(), "front", etag);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.IfNoneMatch.Should().Contain(etag);
    }

    [Fact]
    public async Task GetDocumentImageAsync_Returns_Null_For_A_404()
    {
        var handler = new StubApiHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var response = await client.GetDocumentImageAsync(
            Guid.NewGuid(), Guid.NewGuid(), "front", null);

        response.Should().BeNull("a 404 from the API must be reported as 'not found'");
    }

    [Fact]
    public async Task GetDocumentImageAsync_Returns_The_Body_And_Headers_For_A_200()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new StubApiHandler(_ => BuildOk(body));
        var client = CreateClient(handler);

        var response = await client.GetDocumentImageAsync(
            Guid.NewGuid(), Guid.NewGuid(), "front", null);

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        response.Headers.ETag.Should().NotBeNull();
        response.Content.Headers.LastModified.Should().NotBeNull();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(body);
    }

    // ============== DocumentsController.Image ==============

    /// <summary>
    /// Builds the controller with a stubbed API client so the MVC layer can be driven
    /// without spinning up the whole application.
    /// </summary>
    private static DocumentsController CreateController(HttpMessageHandler handler, out UsersApiClient api)
    {
        api = CreateClient(handler);
        var controller = new DocumentsController(api, NullLogger<DocumentsController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task Image_Returns_304_When_The_Api_Answers_Not_Modified()
    {
        var handler = new StubApiHandler(_ => BuildNotModified());
        var controller = CreateController(handler, out _);

        var result = await controller.Image(Guid.NewGuid(), Guid.NewGuid(), "front");

        var status = result.Should().BeOfType<StatusCodeResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status304NotModified);

        // The caching headers must be copied onto the MVC response.
        controller.Response.Headers.ETag.ToString().Should().Contain("abcdef0123456789");
        controller.Response.Headers.CacheControl.ToString().Should().Contain("no-cache");
    }

    [Fact]
    public async Task Image_Returns_NotFound_When_The_Api_Returns_404()
    {
        var handler = new StubApiHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var controller = CreateController(handler, out _);

        var result = await controller.Image(Guid.NewGuid(), Guid.NewGuid(), "front");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Image_Returns_NotFound_For_An_Invalid_Side()
    {
        var controller = CreateController(new StubApiHandler(_ => BuildOk(new byte[] { 1 })), out _);

        var result = await controller.Image(Guid.NewGuid(), Guid.NewGuid(), "side");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Image_Returns_The_File_And_Copies_The_Caching_Headers()
    {
        var body = new byte[] { 9, 8, 7 };
        var handler = new StubApiHandler(_ => BuildOk(body));
        var controller = CreateController(handler, out _);

        var result = await controller.Image(Guid.NewGuid(), Guid.NewGuid(), "front");

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("image/jpeg");

        controller.Response.Headers.ETag.ToString().Should().Contain("abcdef0123456789");
        controller.Response.Headers.LastModified.ToString().Should().NotBeNullOrEmpty();
        controller.Response.Headers.CacheControl.ToString().Should().Contain("no-cache");
    }
}
