using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using UserManagement.Api.Exceptions;

namespace UserManagement.Api.Middleware;

/// <summary>
/// Global exception handler registered via <c>app.UseExceptionHandler()</c>. It maps
/// domain exceptions to RFC 7807 ProblemDetails responses so we never leak stack traces
/// or internal details to clients. Runs as the outermost exception handler.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger,
                                  ProblemDetailsFactory problemDetailsFactory)
    {
        _logger = logger;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, errors) = MapException(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path} (traceId={TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
        }
        else
        {
            _logger.LogInformation("Request {Method} {Path} mapped to {Status} (traceId={TraceId}, type={Type})",
                httpContext.Request.Method, httpContext.Request.Path, statusCode, httpContext.TraceIdentifier, exception.GetType().Name);
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = _problemDetailsFactory.CreateProblemDetails(
            httpContext,
            statusCode: statusCode,
            title: title,
            type: TypeForStatus(statusCode));

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        await httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

    private static (int Status, string Title, IReadOnlyDictionary<string, string[]>? Errors) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException vex =>
                (StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ.", vex.Errors),

            ConflictException cex =>
                (StatusCodes.Status409Conflict, cex.ErrorMessage,
                    new Dictionary<string, string[]> { [cex.Field] = new[] { cex.ErrorMessage } }),

            NotFoundException nex =>
                (StatusCodes.Status404NotFound, nex.Message,
                    new Dictionary<string, string[]> { ["id"] = new[] { nex.Message } }),

            DbUpdateException dbex when IsUniqueViolation(dbex) =>
                (StatusCodes.Status409Conflict, "Dữ liệu bị trùng.",
                    new Dictionary<string, string[]> { ["conflict"] = new[] { "Dữ liệu bị trùng với bản ghi đã tồn tại." } }),

            BadHttpRequestException bad =>
                (bad.StatusCode == 0 ? StatusCodes.Status400BadRequest : bad.StatusCode,
                 string.IsNullOrWhiteSpace(bad.Message) ? "Yêu cầu không hợp lệ." : bad.Message,
                 null),

            _ => (StatusCodes.Status500InternalServerError, "Đã xảy ra lỗi không mong muốn.", null)
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner.GetType().Name == "SqlException")
            {
                var number = (int?)inner.GetType().GetProperty("Number")?.GetValue(inner);
                if (number is 2627 or 2601)
                {
                    return true;
                }
            }
            inner = inner.InnerException;
        }
        return false;
    }

    private static string TypeForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        _ => "https://tools.ietf.org/html/rfc9110#section-15"
    };
}
