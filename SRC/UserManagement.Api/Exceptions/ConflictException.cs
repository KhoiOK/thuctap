namespace UserManagement.Api.Exceptions;

/// <summary>
/// Thrown when a uniqueness constraint is violated (email, phone, document number, etc).
/// Maps to HTTP 409 with a field-level error.
/// </summary>
public class ConflictException : Exception
{
    public string Field { get; }
    public string ErrorMessage { get; }

    public ConflictException(string field, string errorMessage)
        : base(errorMessage)
    {
        Field = field;
        ErrorMessage = errorMessage;
    }
}
