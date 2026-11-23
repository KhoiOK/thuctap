using Microsoft.AspNetCore.Mvc;

namespace UserManagement.Api.Controllers;

/// <summary>
/// Health-check controller. Concrete CRUD endpoints (Users, IdentityDocuments, Images)
/// will be added in Phases 3 and 4. This controller exists so that the API has at
/// least one reachable endpoint during Phase 1 verification.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "UserManagement.Api",
        version = "v1",
        utcNow = DateTime.UtcNow
    });
}
