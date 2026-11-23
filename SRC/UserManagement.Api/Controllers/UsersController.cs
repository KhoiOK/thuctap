using Microsoft.AspNetCore.Mvc;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Exceptions;
using UserManagement.Api.Services;

namespace UserManagement.Api.Controllers;

/// <summary>
/// Implements API-01 through API-05 from <c>API-Spec.md</c>. Identity-document endpoints
/// are added in Phase 4.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    // API-01
    [HttpGet]
    [ProducesResponseType(typeof(PagedResultDto<UserListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResultDto<UserListItemDto>>> List(
        [FromQuery] UserListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _users.GetPagedAsync(query, cancellationToken);
        return Ok(result);
    }

    // API-02
    [HttpGet("{id:guid}", Name = "GetUserById")]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _users.GetByIdAsync(id, cancellationToken);
        return Ok(result);
    }

    // API-03
    [HttpPost]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailDto>> Create(
        [FromBody] UserUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["body"] = new[] { "Request body is required." }
            });
        }

        var created = await _users.CreateAsync(request, cancellationToken);
        return CreatedAtRoute("GetUserById", new { id = created.Id }, created);
    }

    // API-04
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDetailDto>> Update(
        Guid id,
        [FromBody] UserUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["body"] = new[] { "Request body is required." }
            });
        }

        var updated = await _users.UpdateAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    // API-05
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ok = await _users.SoftDeleteAsync(id, cancellationToken);
        if (!ok)
        {
            throw new NotFoundException("Người dùng không tồn tại hoặc đã bị xóa.");
        }
        return NoContent();
    }
}
