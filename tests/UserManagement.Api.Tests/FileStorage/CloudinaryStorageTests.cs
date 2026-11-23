using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UserManagement.Api.FileStorage;
using Xunit;

namespace UserManagement.Api.Tests.FileStorage;

/// <summary>
/// Unit tests for <see cref="CloudinaryStorage"/>. The vendor is replaced by a stub
/// <see cref="HttpMessageHandler"/> so the request shape (signed parameters, endpoint)
/// can be asserted without network access.
/// </summary>
public class CloudinaryStorageTests
{
    private const string CloudName = "demo-cloud";
    private const string ApiKey = "1234567890";
    private const string ApiSecret = "s3cr3t";

    private static CloudinaryStorage Create(
        HttpMessageHandler handler,
        Action<CloudinaryOptions>? configure = null)
    {
        var options = new CloudinaryOptions
        {
            CloudName = CloudName,
            ApiKey = ApiKey,
            ApiSecret = ApiSecret,
            Folder = "user-management",
            UseSecureDelivery = true,
            AutoInvalidate = true
        };
        configure?.Invoke(options);

        var http = new HttpClient(handler);
        return new CloudinaryStorage(
            http,
            Options.Create(options),
            NullLogger<CloudinaryStorage>.Instance);
    }

    // ---------- Configuration ----------

    [Fact]
    public void IsConfigured_Requires_All_Three_Settings()
    {
        Create(new StubHandler(_ => Ok("{}"))).IsConfigured.Should().BeTrue();

        Create(new StubHandler(_ => Ok("{}")),
            o => o.ApiSecret = "").IsConfigured.Should().BeFalse();

        Create(new StubHandler(_ => Ok("{}")),
            o => o.CloudName = "").IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Upload_Throws_When_Not_Configured()
    {
        var storage = Create(new StubHandler(_ => Ok("{}")), o => o.ApiKey = "");

        var act = () => storage.UploadAsync("tmp/x", "front", new MemoryStream(new byte[] { 1 }), "image/jpeg", default);

        await act.Should().ThrowAsync<CloudinaryException>();
    }

    // ---------- Upload ----------

    [Fact]
    public async Task Upload_Posts_To_Upload_Endpoint_And_Returns_PublicId()
    {
        var handler = new StubHandler(_ => Ok("""
            {"public_id":"user-management/tmp/abc/front-1","version":1234,"format":"jpg","bytes":900,"width":800,"height":600}
            """));

        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        result.PublicId.Should().Be("user-management/tmp/abc/front-1");
        result.Version.Should().Be(1234);
        result.Size.Should().Be(900);
        result.Width.Should().Be(800);
        result.Height.Should().Be(600);
        result.ContentType.Should().Be("image/jpeg");

        handler.LastRequestUri.Should().Be($"https://api.cloudinary.com/v1_1/{CloudName}/image/upload");
    }

    [Fact]
    public async Task Upload_Sends_Signed_Parameters()
    {
        var handler = new StubHandler(_ => Ok("""{"public_id":"x/y","version":1,"format":"jpg","bytes":1,"width":1,"height":1}"""));
        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        await storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        var body = handler.LastRequestBody!;
        body.Should().Contain("name=api_key");
        body.Should().Contain(ApiKey);
        body.Should().Contain("name=folder");
        body.Should().Contain("user-management/tmp/abc");
        body.Should().Contain("name=timestamp");
        body.Should().Contain("name=signature");
        body.Should().Contain("name=allowed_formats");
        body.Should().Contain("name=file");
    }

    [Fact]
    public async Task Upload_Signed_Parameters_Are_Exactly_The_Posted_Parameters()
    {
        // Regression guard. Cloudinary signs every posted parameter except
        // file/api_key, so a mismatch (e.g. allowed_formats appearing twice) makes
        // Cloudinary answer "Invalid Signature". The multipart body must therefore
        // contain each signed parameter exactly once, with the signed value.
        var handler = new StubHandler(_ => Ok("""{"public_id":"x/y","version":1,"format":"jpg","bytes":1,"width":1,"height":1}"""));
        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        await storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        var body = handler.LastRequestBody!;

        // allowed_formats must appear once, and its value must not be doubled.
        body.Should().Contain("jpg,png");
        body.Should().NotContain("jpg,png,jpg,png");

        // Each signed parameter is present exactly once (name= occurrences).
        CountOccurrences(body, "name=allowed_formats").Should().Be(1);
        CountOccurrences(body, "name=folder").Should().Be(1);
        CountOccurrences(body, "name=timestamp").Should().Be(1);
        CountOccurrences(body, "name=use_filename").Should().Be(1);
        CountOccurrences(body, "name=unique_filename").Should().Be(1);
        CountOccurrences(body, "name=public_id").Should().Be(1);
    }

    [Fact]
    public async Task Upload_Uses_Authenticated_Type_When_Secure_Delivery_Is_Enabled()
    {
        // `type=authenticated` is what actually protects the asset. `access_mode` is
        // ignored by the Cloudinary API and would leave the image publicly readable.
        var handler = new StubHandler(_ => Ok("""{"public_id":"x/y","version":1,"format":"jpg","bytes":1,"width":1,"height":1}"""));
        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        await storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        var body = handler.LastRequestBody!;
        body.Should().Contain("name=type");
        body.Should().Contain("authenticated");
        CountOccurrences(body, "name=type").Should().Be(1);
    }

    [Fact]
    public async Task Upload_Omits_Authenticated_Type_When_Secure_Delivery_Is_Disabled()
    {
        var handler = new StubHandler(_ => Ok("""{"public_id":"x/y","version":1,"format":"jpg","bytes":1,"width":1,"height":1}"""));
        var storage = Create(handler, o => o.UseSecureDelivery = false);
        using var source = new MemoryStream(new byte[] { 1 });

        await storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        handler.LastRequestBody!.Should().NotContain("name=type");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public async Task Upload_Throws_On_Error_Status()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"bad"}}""")
        });

        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        await act.Should().ThrowAsync<CloudinaryException>();
    }

    [Fact]
    public async Task Upload_Error_Message_Includes_Cloudinary_Detail()
    {
        // Cloudinary reports config problems in the body, e.g. "Invalid cloud_name cccd".
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"Invalid cloud_name cccd"}}""")
        });

        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        var ex = await act.Should().ThrowAsync<CloudinaryException>();
        ex.Which.Message.Should().Contain("Invalid cloud_name cccd");
        ex.Which.Message.Should().Contain("401");
    }

    [Theory]
    [InlineData("""{"error":{"message":"Invalid cloud_name cccd"}}""", "Invalid cloud_name cccd")]
    [InlineData("""{"error":"plain string"}""", "plain string")]
    [InlineData("""{"message":"top level"}""", "")]
    [InlineData("<html>404</html>", "")]
    [InlineData("", "")]
    public void ExtractErrorMessage_Parses_Cloudinary_Error_Payload(string body, string expected)
    {
        CloudinaryStorage.ExtractErrorMessage(body).Should().Be(expected);
    }

    [Theory]
    [InlineData("Invalid cloud_name cccd", "cccd")]
    [InlineData("cloud_name mismatch", "cccd")]
    public void BuildHint_Points_At_CloudName(string detail, string cloudName)
    {
        var hint = CloudinaryStorage.BuildHint(detail, cloudName);

        hint.Should().Contain("CloudName");
        hint.Should().Contain(cloudName);
    }

    [Fact]
    public void BuildHint_Points_At_ApiSecret_For_Signature_Errors()
    {
        CloudinaryStorage.BuildHint("Invalid Signature abc", "cccd")
            .Should().Contain("ApiSecret");
    }

    [Fact]
    public void BuildHint_Is_Empty_For_Unrelated_Errors()
    {
        CloudinaryStorage.BuildHint("File size too large", "cccd").Should().BeEmpty();
        CloudinaryStorage.BuildHint("", "cccd").Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_CloudName_Mismatch_Explains_How_To_Fix()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"cloud_name mismatch"}}""")
        });

        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        var ex = await act.Should().ThrowAsync<CloudinaryException>();
        ex.Which.Message.Should().Contain("cloud_name mismatch");
        ex.Which.Message.Should().Contain("CloudName");
    }

    [Fact]
    public async Task Upload_Throws_When_PublicId_Is_Missing()
    {
        var handler = new StubHandler(_ => Ok("""{"version":1,"format":"jpg"}"""));
        var storage = Create(handler);
        using var source = new MemoryStream(new byte[] { 1 });

        var act = () => storage.UploadAsync("tmp/abc", "front", source, "image/jpeg", default);

        await act.Should().ThrowAsync<CloudinaryException>();
    }

    // ---------- Move ----------

    [Fact]
    public async Task Move_Posts_To_Rename_Endpoint()
    {
        var handler = new StubHandler(_ => Ok("""
            {"public_id":"user-management/identity-documents/uuid/front-1","version":9,"format":"jpg","bytes":10,"width":2,"height":3}
            """));

        var storage = Create(handler);

        var result = await storage.MoveAsync("user-management/tmp/abc/front-1", "identity-documents/uuid", default);

        result.PublicId.Should().Be("user-management/identity-documents/uuid/front-1");
        handler.LastRequestUri.Should().Be($"https://api.cloudinary.com/v1_1/{CloudName}/image/rename");
        handler.LastRequestBody.Should().Contain("name=from_public_id");
        handler.LastRequestBody.Should().Contain("user-management/tmp/abc/front-1");
        handler.LastRequestBody.Should().Contain("name=to_public_id");
        handler.LastRequestBody.Should().Contain("user-management/identity-documents/uuid/front-1");
    }

    [Fact]
    public async Task Move_Sends_Authenticated_Type()
    {
        // The source asset was uploaded with type `authenticated`; without repeating the
        // type Cloudinary answers "Resource not found".
        var handler = new StubHandler(_ => Ok("""
            {"public_id":"user-management/identity-documents/uuid/front-1","version":9,"format":"jpg","bytes":10,"width":2,"height":3}
            """));

        var storage = Create(handler);

        await storage.MoveAsync("user-management/tmp/abc/front-1", "identity-documents/uuid", default);

        handler.LastRequestBody.Should().Contain("name=type");
        handler.LastRequestBody.Should().Contain("authenticated");
    }

    [Fact]
    public async Task Move_Throws_On_Error_Status()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}")
        });

        var storage = Create(handler);

        var act = () => storage.MoveAsync("user-management/tmp/abc/front-1", "identity-documents/uuid", default);

        await act.Should().ThrowAsync<CloudinaryException>();
    }

    [Fact]
    public async Task Move_Rejects_Empty_Source()
    {
        var storage = Create(new StubHandler(_ => Ok("{}")));

        var act = () => storage.MoveAsync("   ", "identity-documents/uuid", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_Reports_Failed_Ids_Without_Throwing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}")
        });

        var storage = Create(handler);

        var failed = await storage.DeleteAsync(["a/b", "c/d"], default);

        failed.Should().BeEquivalentTo(["a/b", "c/d"]);
        handler.LastRequestUri.Should().Be($"https://api.cloudinary.com/v1_1/{CloudName}/image/destroy");
    }

    [Fact]
    public async Task Delete_Sends_Authenticated_Type()
    {
        // Without the matching type the destroy reports "not found" and the image is
        // never removed from the CDN.
        var handler = new StubHandler(_ => Ok("""{"result":"ok"}"""));
        var storage = Create(handler);

        await storage.DeleteAsync(["a/b"], default);

        handler.LastRequestBody.Should().Contain("name=type");
        handler.LastRequestBody.Should().Contain("authenticated");
    }

    [Fact]
    public async Task Delete_Ignores_Blank_Ids()
    {
        var handler = new StubHandler(_ => Ok("""{"result":"ok"}"""));
        var storage = Create(handler);

        var failed = await storage.DeleteAsync(["", "   ", null!], default);

        failed.Should().BeEmpty();
        handler.RequestCount.Should().Be(0);
    }

    // ---------- Delivery URL ----------

    [Fact]
    public void BuildDeliveryUrl_Uses_Authenticated_Delivery_With_Signature()
    {
        var storage = Create(new StubHandler(_ => Ok("{}")));

        var url = storage.BuildDeliveryUrl("user-management/identity-documents/uuid/front-1");

        var expectedSignature = CloudinaryStorage.ComputeDeliverySignature(
            "user-management/identity-documents/uuid/front-1", ApiSecret);

        url.Should().Be(
            $"https://res.cloudinary.com/{CloudName}/image/authenticated/s--{expectedSignature}--/user-management/identity-documents/uuid/front-1");
    }

    [Fact]
    public void ComputeDeliverySignature_Matches_Cloudinary_Url_Signature_Format()
    {
        // base64url(SHA1(public_id + api_secret)) truncated to 8 characters.
        var signature = CloudinaryStorage.ComputeDeliverySignature("sample", ApiSecret);

        var expected = Convert.ToBase64String(
                System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes("sample" + ApiSecret)))
            .Replace('+', '-')
            .Replace('/', '_');

        signature.Should().Be(expected[..8]);
        signature.Should().HaveLength(8);
    }

    [Fact]
    public void BuildDeliveryUrl_Uses_Public_Delivery_When_Disabled()
    {
        var storage = Create(new StubHandler(_ => Ok("{}")), o => o.UseSecureDelivery = false);

        var url = storage.BuildDeliveryUrl("folder/asset");

        url.Should().Be($"https://res.cloudinary.com/{CloudName}/image/upload/folder/asset");
    }

    [Fact]
    public void BuildDeliveryUrl_Returns_Empty_For_Blank_Id()
    {
        var storage = Create(new StubHandler(_ => Ok("{}")));

        storage.BuildDeliveryUrl("  ").Should().BeEmpty();
    }

    // ---------- Download ----------

    [Fact]
    public async Task Download_Returns_Content_And_ContentType()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        handler.ResponseContentType = "image/png";

        var storage = Create(handler);

        var result = await storage.DownloadAsync("folder/asset", default);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/png");
        result.Length.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task Download_Returns_Null_On_Not_Found()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("")
        });

        var storage = Create(handler);

        var result = await storage.DownloadAsync("folder/asset", default);

        result.Should().BeNull();
    }

    // ---------- Signature ----------

    [Fact]
    public void ComputeSignature_Is_Sha1_Of_Sorted_Params_Plus_Secret()
    {
        var parameters = new Dictionary<string, string>
        {
            ["timestamp"] = "1700000000",
            ["folder"] = "user-management/tmp",
            ["public_id"] = "abc"
        };

        var signature = CloudinaryStorage.ComputeSignature(parameters, ApiSecret);

        // Sorted: folder=...&public_id=...&timestamp=...
        var expectedPayload = "folder=user-management/tmp&public_id=abc&timestamp=1700000000" + ApiSecret;
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(expectedPayload)))
            .ToLowerInvariant();

        signature.Should().Be(expected);
    }

    [Fact]
    public void ComputeSignature_Matches_Cloudinary_Signature_Version_2()
    {
        // Verified against Cloudinary's own documentation example
        // (https://cloudinary.com/documentation/authentication_signatures):
        // secret = "abcd", timestamp = 1315060510,
        // public_id = "sample_image", eager = "w_400,h_300,c_pad|w_260,h_200,c_crop"
        // -> bfd09f95f331f558cbd1320e67aa8d488770583e
        var parameters = new Dictionary<string, string>
        {
            ["timestamp"] = "1315060510",
            ["public_id"] = "sample_image",
            ["eager"] = "w_400,h_300,c_pad|w_260,h_200,c_crop"
        };

        var signature = CloudinaryStorage.ComputeSignature(parameters, "abcd");

        signature.Should().Be("bfd09f95f331f558cbd1320e67aa8d488770583e");
    }

    [Fact]
    public void ComputeSignature_Matches_Cloudinary_Minimal_Upload_Example()
    {
        // From Cloudinary's documentation (secret = "abcd"): only `timestamp` is signed.
        var parameters = new Dictionary<string, string> { ["timestamp"] = "1315060510" };

        var signature = CloudinaryStorage.ComputeSignature(parameters, "abcd");

        signature.Should().Be("a21ad0f63beb4de2e5575204b79ab90bffb02c10");
    }

    [Fact]
    public void ComputeSignature_Escapes_Ampersand_Inside_Values_Only()
    {
        // Signature v2 escapes `&` -> `%26` inside a value to prevent parameter
        // smuggling. Nothing else is escaped: `,` and `/` stay literal.
        var parameters = new Dictionary<string, string>
        {
            ["timestamp"] = "1700000000",
            ["allowed_formats"] = "jpg,png",
            ["folder"] = "user-management/tmp"
        };

        var signature = CloudinaryStorage.ComputeSignature(parameters, ApiSecret);

        var expectedPayload =
            "allowed_formats=jpg,png&folder=user-management/tmp&timestamp=1700000000" + ApiSecret;
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(expectedPayload)))
            .ToLowerInvariant();

        signature.Should().Be(expected);
    }

    [Fact]
    public void ComputeSignature_Skips_Empty_Values()
    {
        var withEmpty = new Dictionary<string, string>
        {
            ["timestamp"] = "1700000000",
            ["public_id"] = ""
        };
        var withoutEmpty = new Dictionary<string, string> { ["timestamp"] = "1700000000" };

        CloudinaryStorage.ComputeSignature(withEmpty, ApiSecret)
            .Should().Be(CloudinaryStorage.ComputeSignature(withoutEmpty, ApiSecret));
    }

    [Fact]
    public void ComputeSignature_Is_Order_Independent()
    {
        var a = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };
        var b = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };

        CloudinaryStorage.ComputeSignature(a, ApiSecret)
            .Should().Be(CloudinaryStorage.ComputeSignature(b, ApiSecret));
    }

    // ---------- Stub handler ----------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int RequestCount { get; private set; }
        public string? ResponseContentType { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = _responder(request);
            if (ResponseContentType is not null && response.Content is not null)
            {
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ResponseContentType);
            }
            return response;
        }
    }

    /// <summary>Builds a 200 OK JSON response for the stub handler.</summary>
    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
