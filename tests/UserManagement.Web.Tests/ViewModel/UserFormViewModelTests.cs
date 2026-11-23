using FluentAssertions;
using UserManagement.Web.ViewModels.Users;
using Xunit;

namespace UserManagement.Web.Tests.ViewModel;

public class UserFormViewModelTests
{
    [Fact]
    public void IsEdit_False_When_Id_Null()
    {
        var vm = new UserFormViewModel { Id = null };
        vm.IsEdit.Should().BeFalse();
        vm.Title.Should().Be("Thêm người dùng");
        vm.SubmitText.Should().Be("Thêm");
    }

    [Fact]
    public void IsEdit_True_When_Id_Set()
    {
        var vm = new UserFormViewModel { Id = Guid.NewGuid() };
        vm.IsEdit.Should().BeTrue();
        vm.Title.Should().Be("Chỉnh sửa người dùng");
        vm.SubmitText.Should().Be("Lưu thay đổi");
    }

    [Fact]
    public void GetFieldError_Returns_First_Error_When_Present()
    {
        var vm = new UserFormViewModel
        {
            FieldErrors = new Dictionary<string, string[]>
            {
                ["email"] = new[] { "Email đã tồn tại.", "Email không hợp lệ." }
            }
        };
        vm.GetFieldError("email").Should().Be("Email đã tồn tại.");
    }

    [Fact]
    public void GetFieldError_Returns_Null_When_Absent()
    {
        var vm = new UserFormViewModel();
        vm.GetFieldError("email").Should().BeNull();
    }

    [Fact]
    public void ApiFieldToForm_Maps_CamelCase_To_PascalCase()
    {
        UserFormViewModel.ApiFieldToForm["email"].Should().Be(nameof(UserFormViewModel.Email));
        UserFormViewModel.ApiFieldToForm["phoneNumber"].Should().Be(nameof(UserFormViewModel.PhoneNumber));
        UserFormViewModel.ApiFieldToForm["fullName"].Should().Be(nameof(UserFormViewModel.FullName));
    }
}
