using FluentAssertions;
using Microsoft.Extensions.Configuration;
using UserManagement.Api.FileStorage;
using Xunit;
using Xunit.Abstractions;

namespace UserManagement.Api.Tests.Infrastructure;

/// <summary>
/// Guards against a subtle ASP.NET Core configuration pitfall: the configuration
/// binder CONCATENATES array values when several configuration sources contribute the
/// same key. That turned <c>AllowedFormats</c> into "jpg,png,jpg,png", which changed
/// the string-to-sign and made Cloudinary reject every upload with "Invalid Signature".
/// </summary>
public class CloudinaryOptionsBindingTests
{
    private readonly ITestOutputHelper _output;

    public CloudinaryOptionsBindingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AllowedFormats_Is_De_Duplicated_Across_Config_Sources()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudinary:AllowedFormats:0"] = "jpg",
                ["Cloudinary:AllowedFormats:1"] = "png"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudinary:AllowedFormats:0"] = "jpg",
                ["Cloudinary:AllowedFormats:1"] = "png"
            })
            .Build();

        var options = new CloudinaryOptions();
        config.GetSection("Cloudinary").Bind(options);

        _output.WriteLine($"Bound: {string.Join(",", options.AllowedFormats)}");

        options.AllowedFormats.Should().BeEquivalentTo(["jpg", "png"]);
    }

    [Fact]
    public void AllowedFormats_Preserves_Order_And_Removes_Only_Duplicates()
    {
        var options = new CloudinaryOptions
        {
            AllowedFormats = ["jpg", "png", "jpg", "JPEG", "png"]
        };

        options.AllowedFormats.Should().Equal("jpg", "png", "JPEG");
    }

    [Fact]
    public void AllowedFormats_Ignores_Blank_Entries()
    {
        var options = new CloudinaryOptions
        {
            AllowedFormats = ["jpg", "", "  ", "png"]
        };

        options.AllowedFormats.Should().Equal("jpg", "png");
    }

    [Fact]
    public void AllowedFormats_Handles_Null()
    {
        var options = new CloudinaryOptions { AllowedFormats = null! };

        options.AllowedFormats.Should().BeEmpty();
    }
}
