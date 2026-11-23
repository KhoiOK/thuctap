namespace UserManagement.Web.Services;

/// <summary>
/// Outcome of an API call. Either contains a successful <typeparamref name="T"/>
/// value or, on failure, a <see cref="ProblemDetailsDto"/> plus the HTTP status code.
/// Field-level validation errors are also exposed via <see cref="FieldErrors"/>.
/// </summary>
public sealed class ApiResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public int StatusCode { get; }
    public ProblemDetailsDto? Problem { get; }

    /// <summary>
    /// Flattened field -> error messages dictionary for binding back to form fields.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; }

    private ApiResult(bool ok, T? value, int statusCode, ProblemDetailsDto? problem, IReadOnlyDictionary<string, string[]> fieldErrors)
    {
        IsSuccess = ok;
        Value = value;
        StatusCode = statusCode;
        Problem = problem;
        FieldErrors = fieldErrors;
    }

    public static ApiResult<T> Ok(T value, int statusCode) => new(true, value, statusCode, null, EmptyErrors);

    public static ApiResult<T> Failure(int statusCode, ProblemDetailsDto? problem) =>
        new(false, default, statusCode, problem, ExtractErrors(problem));

    public string GetDisplayMessage() => Problem?.GetDisplayMessage() ?? $"Lỗi {(int)StatusCode}.";

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors = new Dictionary<string, string[]>();

    private static IReadOnlyDictionary<string, string[]> ExtractErrors(ProblemDetailsDto? problem)
    {
        if (problem?.Errors is null) return EmptyErrors;
        return new Dictionary<string, string[]>(problem.Errors);
    }
}

/// <summary>
/// Result without a payload (used for DELETE/204 responses).
/// </summary>
public sealed class ApiResult
{
    public bool IsSuccess { get; }
    public int StatusCode { get; }
    public ProblemDetailsDto? Problem { get; }

    private ApiResult(bool ok, int status, ProblemDetailsDto? problem)
    {
        IsSuccess = ok;
        StatusCode = status;
        Problem = problem;
    }

    public static ApiResult Ok(int status) => new(true, status, null);
    public static ApiResult Failure(int status, ProblemDetailsDto? problem) => new(false, status, problem);

    public string GetDisplayMessage() => Problem?.GetDisplayMessage() ?? $"Lỗi {StatusCode}.";
}
