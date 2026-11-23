using FluentAssertions;
using UserManagement.Api.Ocr;
using Xunit;

namespace UserManagement.Api.Tests.Ocr;

/// <summary>
/// Unit tests for <see cref="OcrFieldExtractor"/> - the label → field mapping that
/// lives in .NET (see <c>OCR-Cloudinary-Spec.md</c> §3.3).
/// </summary>
public class OcrFieldExtractorTests
{
    [Fact]
    public void Extract_Returns_Empty_When_No_Text()
    {
        var result = OcrFieldExtractor.Extract(null);
        result.Should().BeEmpty();

        result = OcrFieldExtractor.Extract(new Dictionary<string, string>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Extract_Finds_Fields_On_Front_Side()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "SỐ: 079095001234\nHỌ VÀ TÊN: NGUYỄN VĂN A\nNGÀY SINH: 20/04/1995\nGIỚI TÍNH: NAM"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("documentNumber");
        result["documentNumber"].Value.Should().Be("079095001234");
        result["documentNumber"].Source.Should().Be(OcrFieldSource.Front);

        result.Should().ContainKey("fullName");
        result["fullName"].Value.Should().Be("NGUYỄN VĂN A");

        result.Should().ContainKey("dateOfBirth");
        result["dateOfBirth"].Value.Should().Be("20/04/1995");

        result.Should().ContainKey("gender");
        result["gender"].Value.Should().Be("NAM");
    }

    [Fact]
    public void Extract_Finds_Fields_On_Back_Side()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HỌ VÀ TÊN: NGUYỄN VĂN A",
            ["back"] = "CÓ GIÁ TRỊ ĐẾN NGÀY: 20/04/2036\nSỐ HIỆU: 012345678"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("expiryDate");
        result["expiryDate"].Value.Should().Be("20/04/2036");
        result["expiryDate"].Source.Should().Be(OcrFieldSource.Back);

        result.Should().ContainKey("authorityNo");
        result["authorityNo"].Value.Should().Be("012345678");
    }

    [Fact]
    public void Extract_Ignores_Field_On_Wrong_Side()
    {
        // "Ngày sinh" belongs on the front; a back-side occurrence must not be used.
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HỌ VÀ TÊN: NGUYỄN VĂN A",
            ["back"] = "NGÀY SINH: 20/04/1995"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().NotContainKey("dateOfBirth");
    }

    [Fact]
    public void Extract_Matches_Label_Without_Diacritics()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HO VA TEN: TRAN THI B"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("fullName");
        result["fullName"].Value.Should().Be("TRAN THI B");
    }

    [Fact]
    public void Extract_Matches_Colon_Less_Labels()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "So CCCD 079095001234"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("documentNumber");
        result["documentNumber"].Value.Should().Be("079095001234");
    }

    [Fact]
    public void Extract_Falls_Back_To_Next_Line()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HỌ VÀ TÊN:\nNGUYỄN VĂN A\nNGÀY SINH: 20/04/1995"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("fullName");
        result["fullName"].Value.Should().Be("NGUYỄN VĂN A");
        result.Should().ContainKey("dateOfBirth");
    }

    [Fact]
    public void Extract_Does_Not_Skip_Over_Another_Label_Line()
    {
        // The label line is followed by another label line - there is no value.
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HỌ VÀ TÊN:\nNGÀY SINH: 20/04/1995"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().NotContainKey("fullName");
        result.Should().ContainKey("dateOfBirth");
    }

    [Fact]
    public void Extract_Does_Not_Match_Label_In_The_Middle_Of_A_Line()
    {
        // "so" must not match inside an unrelated word at a non-zero offset.
        var raw = new Dictionary<string, string>
        {
            ["front"] = "PASSPORT 12345"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().NotContainKey("documentNumber");
    }

    [Fact]
    public void Extract_Handles_Crlf_Line_Endings()
    {
        var raw = new Dictionary<string, string>
        {
            ["front"] = "HỌ VÀ TÊN: NGUYỄN VĂN A\r\nNGÀY SINH: 20/04/1995"
        };

        var result = OcrFieldExtractor.Extract(raw);

        result.Should().ContainKey("fullName");
        result.Should().ContainKey("dateOfBirth");
    }

    [Theory]
    [InlineData("HỌ VÀ TÊN", "ho va ten")]
    [InlineData("Số CCCD", "so cccd")]
    [InlineData("Ngày sinh:", "ngay sinh")]
    [InlineData("CÓ GIÁ TRỊ ĐẾN NGÀY", "co gia tri den ngay")]
    public void NormalizeLabel_Strips_Diacritics_And_Punctuation(string input, string expected)
    {
        OcrFieldExtractor.NormalizeLabel(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("  Nguyễn   Văn  A  ", "Nguyễn Văn A")]
    [InlineData(": Giá trị", "Giá trị")]
    [InlineData("---", "")]
    public void CleanValue_Trims_Separators_And_Collapses_Whitespace(string input, string expected)
    {
        OcrFieldExtractor.CleanValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Nguyễn Văn A", "Nguyen Van A")]
    [InlineData("Đặng Thị Hồng", "dang Thi Hong")]
    public void RemoveDiacritics_Maps_Dong_Stroke(string input, string expected)
    {
        OcrFieldExtractor.RemoveDiacritics(input).Should().Be(expected);
    }
}
