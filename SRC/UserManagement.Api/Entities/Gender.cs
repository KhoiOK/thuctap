namespace UserManagement.Api.Entities;

/// <summary>
/// Gender enumeration. Stored as a number in the database (1=Male, 2=Female, 3=Other)
/// and exposed as a string via the API (see API-Spec §1.1).
/// </summary>
public enum Gender
{
    Male = 1,
    Female = 2,
    Other = 3
}
