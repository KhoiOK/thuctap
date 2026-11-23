namespace UserManagement.Api.Exceptions;

/// <summary>
/// Thrown when a domain entity cannot be located. Maps to HTTP 404.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
