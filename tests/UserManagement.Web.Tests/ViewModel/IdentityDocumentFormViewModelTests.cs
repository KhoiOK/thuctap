using FluentAssertions;
using UserManagement.Web.ViewModels.Users;
using Xunit;

namespace UserManagement.Web.Tests.ViewModel;

public class IdentityDocumentFormViewModelTests
{
    [Fact]
    public void IsEdit_False_When_DocumentId_Null()
    {
        var vm = new IdentityDocumentFormViewModel { UserId = Guid.NewGuid() };
        vm.IsEdit.Should().BeFalse();
        vm.Title.Should().Be("Thêm giấy tờ");
        vm.SubmitText.Should().Be("Thêm");
    }

    [Fact]
    public void IsEdit_True_When_DocumentId_Set()
    {
        var vm = new IdentityDocumentFormViewModel
        {
            UserId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid()
        };
        vm.IsEdit.Should().BeTrue();
        vm.Title.Should().Be("Chỉnh sửa giấy tờ");
        vm.SubmitText.Should().Be("Lưu thay đổi");
    }

    [Theory]
    [InlineData("CCCD", true)]
    [InlineData("CMND", true)]
    [InlineData("cccd", true)]
    [InlineData("cmnd", true)]
    public void ShowsBackImageInput_True_For_CCCD_CMND_In_Edit_Mode(string type, bool expected)
    {
        var vm = new IdentityDocumentFormViewModel
        {
            UserId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentType = type
        };
        vm.ShowsBackImageInput.Should().Be(expected);
    }

    [Fact]
    public void ShowsBackImageInput_False_For_Passport_In_Edit_Mode()
    {
        // Even in edit mode, the back-image input is hidden for passports.
        var vm = new IdentityDocumentFormViewModel
        {
            UserId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentType = "Passport"
        };
        vm.ShowsBackImageInput.Should().BeFalse();
    }

    [Fact]
    public void ShowsBackImageInput_True_In_Create_Mode_Regardless_Of_Type()
    {
        // The Create view's JS toggles the back-image input based on the selected type.
        // The view-model just signals "may show", the JS hides it for Passport.
        var vm = new IdentityDocumentFormViewModel { UserId = Guid.NewGuid(), DocumentType = "Passport" };
        vm.ShowsBackImageInput.Should().BeTrue();
    }
}
