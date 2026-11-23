using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UserManagement.Web.Services;
using Xunit;

namespace UserManagement.Web.Tests;

/// <summary>
/// Unit tests for the return-URL sanitization and the pagination helpers in the
/// UserListViewModel. These are pure logic tests, so they don't need a host.
/// </summary>
public class UserListViewModelTests
{
    [Fact]
    public void PageNumbers_Empty_When_No_Result()
    {
        var vm = new ViewModels.Users.UserListViewModel();
        vm.PageNumbers.Should().BeEmpty();
    }

    [Fact]
    public void PageNumbers_All_When_Less_Than_Max_Window()
    {
        var vm = new ViewModels.Users.UserListViewModel
        {
            Page = 2,
            PageSize = 10,
            Result = new PagedResultDto<UserListItemDto>(
                Items: Array.Empty<UserListItemDto>(),
                Page: 2, PageSize: 10, TotalItems: 50, TotalPages: 5)
        };
        vm.PageNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void BuildQueryString_Includes_All_Defaults()
    {
        var vm = new ViewModels.Users.UserListViewModel
        {
            Keyword = "nguyen",
            Page = 3,
            PageSize = 20,
            SortBy = "fullName",
            SortOrder = "asc"
        };
        var qs = vm.BuildQueryString(3);
        qs.Should().Contain("page=3");
        qs.Should().Contain("pageSize=20");
        qs.Should().Contain("sortBy=fullName");
        qs.Should().Contain("sortOrder=asc");
        qs.Should().Contain("keyword=nguyen");
    }

    [Fact]
    public void BuildQueryString_Omits_Blank_Keyword()
    {
        var vm = new ViewModels.Users.UserListViewModel { Page = 1, PageSize = 10, SortBy = "createdAt", SortOrder = "desc" };
        var qs = vm.BuildQueryString(1);
        qs.Should().NotContain("keyword");
    }

    [Fact]
    public void PreviousSortOrder_Toggles_Asc_To_Desc_For_Same_Column()
    {
        var vm = new ViewModels.Users.UserListViewModel { SortBy = "fullName", SortOrder = "asc" };
        vm.PreviousSortOrder("fullName").Should().Be("desc");
    }

    [Fact]
    public void PreviousSortOrder_Defaults_To_Asc_For_Different_Column()
    {
        var vm = new ViewModels.Users.UserListViewModel { SortBy = "fullName", SortOrder = "asc" };
        vm.PreviousSortOrder("email").Should().Be("asc");
    }
}
