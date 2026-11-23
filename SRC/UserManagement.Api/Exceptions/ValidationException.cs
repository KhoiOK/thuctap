namespace UserManagement.Api.Exceptions;

/// <summary>
/// Thrown when a business rule is violated by user input. Maps to HTTP 400 with
/// field-level errors.
/// </summary>
public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Dữ liệu không hợp lệ.")
    {
        Errors = errors;
    }
}
