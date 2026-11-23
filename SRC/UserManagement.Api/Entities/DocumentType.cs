namespace UserManagement.Api.Entities;

/// <summary>
/// Identity document type. Stored as a number in the database (1=CCCD, 2=CMND, 3=Passport)
/// and exposed as a string via the API (see API-Spec §1.1).
/// </summary>
public enum DocumentType
{
    CCCD = 1,
    CMND = 2,
    Passport = 3
}
