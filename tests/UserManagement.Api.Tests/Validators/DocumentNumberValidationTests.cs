using FluentAssertions;
using UserManagement.Api.Entities;
using UserManagement.Api.Validators;
using Xunit;

namespace UserManagement.Api.Tests.Validators;

/// <summary>
/// Unit tests for the BR-08 document-number rules in <see cref="NormalizationHelpers"/>.
/// </summary>
public class DocumentNumberValidationTests
{
    // ============== CCCD ==============

    [Theory]
    [InlineData("001098123456")]
    [InlineData("123456789012")]
    [InlineData("000000000000")]
    public void Cccd_Twelve_Digits_Is_Valid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.CCCD, value).Should().BeTrue();
    }

    [Theory]
    [InlineData("12345678901")]   // 11 digits
    [InlineData("1234567890123")] // 13 digits
    [InlineData("12345abc67890")] // contains letters
    [InlineData("")]
    [InlineData("   ")]
    public void Cccd_Other_Formats_Are_Invalid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.CCCD, value).Should().BeFalse();
    }

    // ============== CMND ==============

    [Theory]
    [InlineData("123456789")]    // 9 digits
    [InlineData("123456789012")]  // 12 digits
    [InlineData("000000000")]
    public void Cmnd_Nine_Or_Twelve_Digits_Is_Valid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.CMND, value).Should().BeTrue();
    }

    [Theory]
    [InlineData("12345678")]      // 8 digits
    [InlineData("1234567890")]    // 10 digits
    [InlineData("1234567890a")]   // 10 chars with letter
    public void Cmnd_Other_Lengths_Are_Invalid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.CMND, value).Should().BeFalse();
    }

    // ============== Passport ==============

    [Theory]
    [InlineData("A1234567")]
    [InlineData("AB123456")]
    [InlineData("123456789")]
    [InlineData("ABC123")]
    [InlineData("A1B2C3D4E")]
    public void Passport_Six_To_Nine_Alphanumeric_Is_Valid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.Passport, value).Should().BeTrue();
    }

    [Theory]
    [InlineData("ABCDE")]        // 5 chars
    [InlineData("ABCDEFGHIJ")]   // 10 chars
    [InlineData("ABC-123")]      // contains hyphen
    [InlineData("ABC 123")]      // contains space
    [InlineData("ABC*123")]      // contains non-alphanumeric
    public void Passport_Other_Formats_Are_Invalid(string value)
    {
        NormalizationHelpers.IsValidDocumentNumber(DocumentType.Passport, value).Should().BeFalse();
    }

    [Fact]
    public void Passport_Lowercase_Is_Uppercased_During_Normalization()
    {
        var normalized = NormalizationHelpers.NormalizeDocumentNumber(DocumentType.Passport, "ab12345");
        normalized.Should().Be("AB12345");
    }

    [Fact]
    public void Passport_Trims_Whitespace()
    {
        var normalized = NormalizationHelpers.NormalizeDocumentNumber(DocumentType.Passport, "  AB12345  ");
        normalized.Should().Be("AB12345");
    }

    [Fact]
    public void Cccd_Does_Not_Uppercase()
    {
        var normalized = NormalizationHelpers.NormalizeDocumentNumber(DocumentType.CCCD, "  001098123456  ");
        normalized.Should().Be("001098123456");
    }
}
