using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UserManagement.Api.Exceptions;

namespace UserManagement.Api.Middleware;

/// <summary>
/// Translates ASP.NET Core's built-in <see cref="Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary"/>
/// into a ProblemDetails 400 response with camelCase field names. Runs as an MVC filter so
/// DataAnnotations, query-binding and JSON-binding errors all flow through the same shape as
/// domain validation errors.
/// </summary>
public class ModelStateValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (rawKey, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var key = string.IsNullOrEmpty(rawKey) ? "body" : NormalizeKey(rawKey);
            if (!errors.TryGetValue(key, out var list))
            {
                list = new List<string>();
                errors[key] = list;
            }
            foreach (var err in entry.Errors)
            {
                var message = !string.IsNullOrWhiteSpace(err.ErrorMessage)
                    ? err.ErrorMessage
                    : err.Exception?.Message ?? "Giá trị không hợp lệ.";
                list.Add(message);
            }
        }

        if (errors.Count > 0)
        {
            var problem = new ValidationProblemDetails(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            };
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" }
            };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static string NormalizeKey(string key)
    {
        // ModelState uses PascalCase for top-level property names (e.g. "Email", "PhoneNumber")
        // and a dot-separated path for nested objects. We convert the first segment to camelCase
        // and keep the rest as-is so the field name matches the JSON property name.
        var parts = key.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = i == 0 ? ToCamel(parts[i]) : parts[i];
        }
        return string.Join('.', parts);
    }

    private static string ToCamel(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return char.ToLowerInvariant(value[0]) + value[1..];
    }
}
