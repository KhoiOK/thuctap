using FluentAssertions;
using Microsoft.Extensions.Options;
using UserManagement.Api.Entities;
using UserManagement.Api.Ocr;
using Xunit;

namespace UserManagement.Api.Tests.Ocr;

/// <summary>
/// Unit tests for <see cref="OcrNormalizer"/> - the layer that turns raw OCR strings into
/// values that satisfy the existing BR-* business rules.
/// </summary>
public class OcrNormalizerTests
{
    private static readonly DateTime Today = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    private static OcrNormalizer Create(bool allowTwoDigitYearInference = true)
    {
        var options = Options.Create(new OcrOptions
        {
            Enabled = true,
            AllowTwoDigitYearInference = allowTwoDigitYearInference
        });
        return new OcrNormalizer(options, TimeProvider.System);
    }

    private static Dictionary<string, ExtractedValue> Extract(params (string Key, string Value, OcrFieldSource Source)[] values)
    {
        return values.ToDictionary(
            v => v.Key,
            v => new ExtractedValue(v.Value, v.Source),
            StringComparer.Ordinal);
    }

    // ---------- Document number ----------

    [Fact]
    public void DocumentNumber_Keeps_12_Digits_For_Cccd()
    {
        var result = Create().Normalize(
            Extract(("documentNumber", "079095001234", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["documentNumber"].Value.Should().Be("079095001234");
        result["documentNumber"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void DocumentNumber_Strips_Non_Digit_Characters()
    {
        var result = Create().Normalize(
            Extract(("documentNumber", "079 095 001 234", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["documentNumber"].Value.Should().Be("079095001234");
        result["documentNumber"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void DocumentNumber_Truncates_Extra_Digits_To_Last_12()
    {
        // 16 digits - the leading 4 are noise from the OCR engine, the trailing
        // 12 are the real document number.
        var result = Create().Normalize(
            Extract(("documentNumber", "1234079095001234", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["documentNumber"].Value.Should().Be("079095001234");
        result["documentNumber"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void DocumentNumber_Short_Number_Is_Invalid()
    {
        var result = Create().Normalize(
            Extract(("documentNumber", "12345", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["documentNumber"].Value.Should().Be("12345");
        result["documentNumber"].IsValid.Should().BeFalse();
    }

    [Fact]
    public void DocumentNumber_Empty_Raw_Stays_Null()
    {
        var result = Create().Normalize(
            Extract(("documentNumber", "   ", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["documentNumber"].Value.Should().BeNull();
        result["documentNumber"].IsValid.Should().BeFalse();
    }

    // ---------- Full name ----------

    [Fact]
    public void FullName_Collapses_Whitespace()
    {
        var result = Create().Normalize(
            Extract(("fullName", "  NGUYỄN    VĂN  A  ", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["fullName"].Value.Should().Be("NGUYỄN VĂN A");
        result["fullName"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void FullName_Single_Character_Is_Invalid()
    {
        var result = Create().Normalize(
            Extract(("fullName", "A", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["fullName"].IsValid.Should().BeFalse();
    }

    // ---------- Gender ----------

    [Theory]
    [InlineData("NAM", "Male")]
    [InlineData("Nam", "Male")]
    [InlineData("NỮ", "Female")]
    [InlineData("Nu", "Female")]
    [InlineData("KHÁC", "Other")]
    public void Gender_Maps_Vietnamese_Labels(string raw, string expected)
    {
        var result = Create().Normalize(
            Extract(("gender", raw, OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["gender"].Value.Should().Be(expected);
        result["gender"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void Gender_Unknown_Label_Is_Invalid()
    {
        var result = Create().Normalize(
            Extract(("gender", "XYZ", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["gender"].Value.Should().BeNull();
        result["gender"].IsValid.Should().BeFalse();
    }

    // ---------- Dates ----------

    [Theory]
    [InlineData("20/04/1995", "1995-04-20")]
    [InlineData("20-04-1995", "1995-04-20")]
    [InlineData("20.04.1995", "1995-04-20")]
    public void Date_Parses_Common_Separators(string raw, string expected)
    {
        var result = Create().Normalize(
            Extract(("dateOfBirth", raw, OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["dateOfBirth"].Value.Should().Be(expected);
        result["dateOfBirth"].IsValid.Should().BeTrue();
        result["dateOfBirth"].IsInferred.Should().BeFalse();
    }

    [Fact]
    public void Date_With_Two_Digit_Year_Is_Inferred_And_Flagged()
    {
        var result = Create().Normalize(
            Extract(("dateOfBirth", "20/04/95", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["dateOfBirth"].Value.Should().Be("1995-04-20");
        result["dateOfBirth"].IsInferred.Should().BeTrue();
    }

    [Fact]
    public void Date_With_Two_Digit_Year_Is_Rejected_When_Disabled()
    {
        var result = Create(allowTwoDigitYearInference: false).Normalize(
            Extract(("dateOfBirth", "20/04/95", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["dateOfBirth"].Value.Should().BeNull();
        result["dateOfBirth"].IsValid.Should().BeFalse();
    }

    [Fact]
    public void Date_That_Is_Not_A_Date_Is_Rejected()
    {
        var result = Create().Normalize(
            Extract(("dateOfBirth", "không rõ", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["dateOfBirth"].Value.Should().BeNull();
        result["dateOfBirth"].IsValid.Should().BeFalse();
    }

    [Fact]
    public void Date_Keeps_The_Side_It_Was_Read_From()
    {
        // On the real CCCD the expiry date is printed on the FRONT and the issue date
        // on the BACK; the date of birth is on the FRONT. The side must survive the
        // normalization instead of being hard-coded to "Front".
        var result = Create().Normalize(
            Extract(
                ("expiryDate", "20/04/2036", OcrFieldSource.Front),
                ("issueDate", "10/05/2021", OcrFieldSource.Back),
                ("dateOfBirth", "20/04/1995", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["expiryDate"].Source.Should().Be(OcrFieldSource.Front);
        result["issueDate"].Source.Should().Be(OcrFieldSource.Back);
        result["dateOfBirth"].Source.Should().Be(OcrFieldSource.Front);
    }

    [Fact]
    public void Date_That_Cannot_Be_Parsed_Keeps_Its_Side()
    {
        var result = Create().Normalize(
            Extract(("expiryDate", "không rõ", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["expiryDate"].Value.Should().BeNull();
        result["expiryDate"].Source.Should().Be(OcrFieldSource.Front);
    }

    [Fact]
    public void Date_Out_Of_Range_Is_Rejected()
    {
        var result = Create().Normalize(
            Extract(("dateOfBirth", "01/01/1850", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["dateOfBirth"].Value.Should().BeNull();
    }

    // ---------- Address / authority ----------

    [Fact]
    public void Address_Is_Truncated_To_500_Characters()
    {
        var longAddress = new string('a', 600);
        var result = Create().Normalize(
            Extract(("address", longAddress, OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["address"].Value.Should().HaveLength(500);
        result["address"].IsValid.Should().BeTrue();
    }

    [Fact]
    public void Text_Fields_Keep_The_Side_They_Were_Read_From()
    {
        // The document number, the name and the gender are read from the front, but
        // the MRZ fallback can deliver the number from the back - the side must not
        // be hard-coded to "Front" for any field.
        var result = Create().Normalize(
            Extract(
                ("documentNumber", "079095001234", OcrFieldSource.Back),
                ("fullName", "NGUYỄN VĂN A", OcrFieldSource.Back),
                ("gender", "Nam", OcrFieldSource.Back),
                ("address", "12 Lê Lợi", OcrFieldSource.Back)),
            DocumentType.CCCD);

        result["documentNumber"].Source.Should().Be(OcrFieldSource.Back);
        result["fullName"].Source.Should().Be(OcrFieldSource.Back);
        result["gender"].Source.Should().Be(OcrFieldSource.Back);
        result["address"].Source.Should().Be(OcrFieldSource.Back);
    }

    [Fact]
    public void AuthorityNo_Is_Taken_From_Back_Side()
    {
        var result = Create().Normalize(
            Extract(("authorityNo", "012345678", OcrFieldSource.Back)),
            DocumentType.CCCD);

        result["authorityNo"].Value.Should().Be("012345678");
        result["authorityNo"].Source.Should().Be(OcrFieldSource.Back);
    }

    // ---------- Confidence ----------

    [Fact]
    public void Confidence_Is_Carried_Over()
    {
        var extracted = Extract(("fullName", "NGUYỄN VĂN A", OcrFieldSource.Front));
        var confidences = new Dictionary<string, double> { ["fullName"] = 0.42 };

        var result = Create().Normalize(extracted, DocumentType.CCCD, confidences);

        result["fullName"].Confidence.Should().Be(0.42);
    }

    [Fact]
    public void Missing_Confidence_Defaults_To_One()
    {
        var result = Create().Normalize(
            Extract(("fullName", "NGUYỄN VĂN A", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result["fullName"].Confidence.Should().Be(1.0);
    }

    [Fact]
    public void Unknown_Field_Is_Passed_Through_Without_Validation()
    {
        var result = Create().Normalize(
            Extract(("someNewField", "value", OcrFieldSource.Front)),
            DocumentType.CCCD);

        result.Should().ContainKey("someNewField");
        result["someNewField"].Value.Should().BeNull();
        result["someNewField"].IsValid.Should().BeFalse();
    }
}
