using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Web.Services;
using UserManagement.Web.ViewModels.Users;

namespace UserManagement.Web.Controllers;

/// <summary>
/// UI-Spec §2: top-level controller for user-facing screens. Base route /Users.
/// All data access goes through <see cref="IUsersApiClient"/>.
/// </summary>
[Route("Users")]
public class UsersController : Controller
{
    private readonly IUsersApiClient _api;
    private readonly ILogger<UsersController> _logger;

    private static readonly string[] AllowedSortBy = { "fullName", "email", "createdAt" };
    private static readonly string[] AllowedSortOrder = { "asc", "desc" };

    public UsersController(IUsersApiClient api, ILogger<UsersController> logger)
    {
        _api = api;
        _logger = logger;
    }

    // =================== SCR-01: List ===================

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        // Clamp inputs to known allowed values so the API doesn't 400.
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            <= 0 => 10,
            > 100 => 100,
            _ => pageSize
        };
        if (!AllowedSortBy.Contains(sortBy, StringComparer.OrdinalIgnoreCase)) sortBy = "createdAt";
        if (!AllowedSortOrder.Contains(sortOrder, StringComparer.OrdinalIgnoreCase)) sortOrder = "desc";

        var vm = new UserListViewModel
        {
            Keyword = keyword,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        try
        {
            var result = await _api.ListAsync(keyword, page, pageSize, sortBy, sortOrder, cancellationToken);
            if (result.IsSuccess)
            {
                vm.Result = result.Value;
            }
            else
            {
                vm.ErrorMessage = result.GetDisplayMessage();
            }
        }
        catch (ApiTransportException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
        return View(vm);
    }

    // =================== SCR-02: Details ===================

    [HttpGet("Details/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, [FromQuery] string? returnUrl, CancellationToken cancellationToken = default)
    {
        var vm = new UserDetailViewModel
        {
            ReturnUrl = SanitizeReturnUrl(returnUrl)
        };
        try
        {
            var result = await _api.GetByIdAsync(id, cancellationToken);
            if (result.IsSuccess)
            {
                vm.User = result.Value;
            }
            else if (result.StatusCode == 404)
            {
                TempData["Error"] = "Người dùng không tồn tại hoặc đã bị xóa.";
                return RedirectToAction(nameof(Index));
            }
            else
            {
                vm.ErrorMessage = result.GetDisplayMessage();
            }
        }
        catch (ApiTransportException ex)
        {
            vm.ErrorMessage = ex.Message;
        }
        return View(vm);
    }

    // =================== SCR-03: Create ===================

    [HttpGet("Create")]
    public IActionResult Create([FromQuery] string? returnUrl)
    {
        var vm = new UserFormViewModel
        {
            ReturnUrl = SanitizeReturnUrl(returnUrl)
        };
        return View(vm);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel vm, CancellationToken cancellationToken = default)
    {
        vm.ReturnUrl = SanitizeReturnUrl(vm.ReturnUrl);

        // Trim user input before sending (UI-Spec §5.3).
        vm.FullName = vm.FullName?.Trim() ?? string.Empty;
        vm.Email = vm.Email?.Trim() ?? string.Empty;
        vm.PhoneNumber = vm.PhoneNumber?.Trim() ?? string.Empty;
        vm.Address = vm.Address?.Trim();

        if (!ModelState.IsValid) return View(vm);

        var request = new UserUpsertRequest(
            FullName: vm.FullName,
            Email: vm.Email,
            PhoneNumber: vm.PhoneNumber,
            DateOfBirth: vm.DateOfBirth,
            Gender: string.IsNullOrEmpty(vm.Gender) ? null : vm.Gender,
            Address: vm.Address);

        try
        {
            var result = await _api.CreateAsync(request, cancellationToken);
            if (result.IsSuccess && result.Value is not null)
            {
                TempData["Toast"] = $"Đã thêm người dùng {result.Value.FullName}.";
                return RedirectToAction(nameof(Details), new { id = result.Value.Id, returnUrl = vm.ReturnUrl });
            }
            MapProblemToModelState(result, vm);
        }
        catch (ApiTransportException ex)
        {
            vm.GeneralError = ex.Message;
        }
        return View(vm);
    }

    // =================== SCR-04: Edit ===================

    [HttpGet("Edit/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromQuery] string? returnUrl, CancellationToken cancellationToken = default)
    {
        var vm = new UserFormViewModel
        {
            Id = id,
            ReturnUrl = SanitizeReturnUrl(returnUrl)
        };
        try
        {
            var result = await _api.GetByIdAsync(id, cancellationToken);
            if (result.IsSuccess && result.Value is not null)
            {
                vm.FullName = result.Value.FullName;
                vm.Email = result.Value.Email;
                vm.PhoneNumber = result.Value.PhoneNumber;
                vm.DateOfBirth = result.Value.DateOfBirth;
                vm.Gender = result.Value.Gender;
                vm.Address = result.Value.Address;
            }
            else if (result.StatusCode == 404)
            {
                TempData["Error"] = "Người dùng không tồn tại hoặc đã bị xóa.";
                return RedirectToAction(nameof(Index));
            }
            else
            {
                vm.GeneralError = result.GetDisplayMessage();
            }
        }
        catch (ApiTransportException ex)
        {
            vm.GeneralError = ex.Message;
        }
        return View(vm);
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, UserFormViewModel vm, CancellationToken cancellationToken = default)
    {
        vm.Id = id;
        vm.ReturnUrl = SanitizeReturnUrl(vm.ReturnUrl);

        vm.FullName = vm.FullName?.Trim() ?? string.Empty;
        vm.Email = vm.Email?.Trim() ?? string.Empty;
        vm.PhoneNumber = vm.PhoneNumber?.Trim() ?? string.Empty;
        vm.Address = vm.Address?.Trim();

        if (!ModelState.IsValid) return View(vm);

        var request = new UserUpsertRequest(
            FullName: vm.FullName,
            Email: vm.Email,
            PhoneNumber: vm.PhoneNumber,
            DateOfBirth: vm.DateOfBirth,
            Gender: string.IsNullOrEmpty(vm.Gender) ? null : vm.Gender,
            Address: vm.Address);

        try
        {
            var result = await _api.UpdateAsync(id, request, cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Toast"] = "Đã lưu thay đổi.";
                return RedirectToAction(nameof(Details), new { id, returnUrl = vm.ReturnUrl });
            }
            MapProblemToModelState(result, vm);
        }
        catch (ApiTransportException ex)
        {
            vm.GeneralError = ex.Message;
        }
        return View(vm);
    }

    // =================== SCR-05: Delete ===================

    [HttpPost("Delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? returnUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _api.DeleteAsync(id, cancellationToken);
            if (result.IsSuccess)
            {
                TempData["Toast"] = "Đã xóa người dùng.";
                var safe = SanitizeReturnUrl(returnUrl);
                if (!string.IsNullOrEmpty(safe)) return Redirect(safe);
                return RedirectToAction(nameof(Index));
            }
            TempData["Error"] = result.GetDisplayMessage();
        }
        catch (ApiTransportException ex)
        {
            TempData["Error"] = ex.Message;
        }
        var safe2 = SanitizeReturnUrl(returnUrl);
        if (!string.IsNullOrEmpty(safe2)) return Redirect(safe2);
        return RedirectToAction(nameof(Index));
    }

    // =================== helpers ===================

    /// <summary>
    /// Returns the URL only if it is a local path under /Users. Otherwise null.
    /// Prevents open redirect (UI-Spec §4.3).
    /// </summary>
    private string? SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;
        if (!Url.IsLocalUrl(returnUrl)) return null;
        if (!returnUrl.StartsWith("/Users", StringComparison.OrdinalIgnoreCase)) return null;
        return returnUrl;
    }

    /// <summary>
    /// Translates API Problem Details into ASP.NET ModelState so the form re-renders
    /// with the same red field-level errors the user would see in Swagger.
    /// </summary>
    private void MapProblemToModelState<T>(ApiResult<T> result, UserFormViewModel vm)
    {
        if (result.StatusCode == 404)
        {
            vm.GeneralError = "Người dùng không tồn tại hoặc đã bị xóa.";
            return;
        }
        if (result.FieldErrors.Count > 0)
        {
            vm.FieldErrors = result.FieldErrors.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var kv in result.FieldErrors)
            {
                if (UserFormViewModel.ApiFieldToForm.TryGetValue(kv.Key, out var formField))
                {
                    foreach (var msg in kv.Value)
                    {
                        ModelState.AddModelError(formField, msg);
                    }
                }
            }
        }
        vm.GeneralError ??= result.GetDisplayMessage();
    }
}
