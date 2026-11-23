using FluentAssertions;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Exceptions;
using UserManagement.Api.Validators;
using Xunit;

namespace UserManagement.Api.Tests.Validators;

/// <summary>
/// Unit tests for <see cref="UserValidator"/>. Covers BR-01..BR-04 normalization and
/// validation rules without needing a database.
/// </summary>
public class UserValidatorTests
{
    [Fact]
    public void Normalize_Email_Trims_And_Lowercases()
    {
        var (_, email, _) = UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "  A.Nguyen@Example.COM ",
            PhoneNumber = "0912345678"
        });
        email.Should().Be("a.nguyen@example.com");
    }

    [Fact]
    public void Normalize_Phone_Strips_Spaces_Dots_Dashes_And_Converts_84_To_0()
    {
        var (_, _, phone) = UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "a@example.com",
            PhoneNumber = "+84 912.345-678"
        });
        phone.Should().Be("0912345678");
    }

    [Fact]
    public void Normalize_Phone_Drops_Leading_84()
    {
        var (_, _, phone) = UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "a@example.com",
            PhoneNumber = "84912345678"
        });
        phone.Should().Be("0912345678");
    }

    [Fact]
    public void Phone_Starting_With_Plus840_Is_Rejected()
    {
        // +840912345678 - after stripping "+84" we get "0912345678" but the spec says
        // "+840912345678 bị từ chối" because the conversion yields "00912345678" which
        // does not match the regex.
        var ex = Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "a@example.com",
            PhoneNumber = "+840912345678"
        }));
        ex.Errors.Should().ContainKey("phoneNumber");
    }

    [Fact]
    public void Empty_FullName_Is_Rejected()
    {
        Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "",
            Email = "a@example.com",
            PhoneNumber = "0912345678"
        }));
    }

    [Fact]
    public void Short_FullName_Is_Rejected()
    {
        Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "A",
            Email = "a@example.com",
            PhoneNumber = "0912345678"
        }));
    }

    [Fact]
    public void Long_FullName_Is_Rejected()
    {
        Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = new string('a', 101),
            Email = "a@example.com",
            PhoneNumber = "0912345678"
        }));
    }

    [Fact]
    public void Future_DateOfBirth_Is_Rejected()
    {
        var ex = Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "a@example.com",
            PhoneNumber = "0912345678",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
        }));
        ex.Errors.Should().ContainKey("dateOfBirth");
    }

    [Fact]
    public void Very_Old_DateOfBirth_Is_Rejected()
    {
        var ex = Assert.Throws<ValidationException>(() => UserValidator.ValidateAndNormalize(new UserUpsertRequest
        {
            FullName = "Nguyen Van A",
            Email = "a@example.com",
            PhoneNumber = "0912345678",
            DateOfBirth = new DateOnly(1899, 12, 31)
        }));
        ex.Errors.Should().ContainKey("dateOfBirth");
    }
}
