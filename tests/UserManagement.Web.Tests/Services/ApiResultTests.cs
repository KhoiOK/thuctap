using FluentAssertions;
using UserManagement.Web.Services;
using Xunit;

namespace UserManagement.Web.Tests.Services;

public class ApiResultTests
{
    [Fact]
    public void Ok_Sets_IsSuccess_And_Value()
    {
        var result = ApiResult<string>.Ok("hello", 200);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Failure_Sets_Problem_And_FieldErrors()
    {
        var problem = new ProblemDetailsDto(
            Type: "https://example.com",
            Title: "Validation failed",
            Status: 400,
            Detail: "One or more fields are invalid.",
            Instance: "/api/v1/users",
            TraceId: "trace-1",
            Errors: new Dictionary<string, string[]>
            {
                ["email"] = new[] { "Email already exists." }
            });
        var result = ApiResult<string>.Failure(400, problem);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.FieldErrors.Should().ContainKey("email");
    }

    [Fact]
    public void GetDisplayMessage_Prefers_Detail_Then_Title_Then_Default()
    {
        var withDetail = new ProblemDetailsDto(null, "T", 400, "detail msg", null, null, null);
        withDetail.GetDisplayMessage().Should().Be("detail msg");

        var withOnlyTitle = new ProblemDetailsDto(null, "T", 409, null, null, null, null);
        withOnlyTitle.GetDisplayMessage().Should().Be("T");

        var withOnlyStatus = new ProblemDetailsDto(null, null, 404, null, null, null, null);
        withOnlyStatus.GetDisplayMessage().Should().Be("Không tìm thấy dữ liệu.");
    }
}
