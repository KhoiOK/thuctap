using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Web.Models;

namespace UserManagement.Web.Controllers;

/// <summary>
/// Hosts the generic error page used by the global exception handler
/// (<c>app.UseExceptionHandler("/Home/Error")</c>). Without this controller the
/// handler would redirect to a 404 page.
/// </summary>
public class HomeController : Controller
{
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View(new ErrorViewModel { RequestId = requestId });
    }
}
