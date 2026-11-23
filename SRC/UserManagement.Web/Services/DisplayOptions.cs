namespace UserManagement.Web.Services;

/// <summary>
/// Display-related options (time zone, etc.) loaded from configuration.
/// </summary>
public class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>
    /// Windows time zone id (e.g. "SE Asia Standard Time"). Cross-platform ids can also be used
    /// (e.g. "Asia/Ho_Chi_Minh") - they will be normalized via TryFindSystemTimeZoneById.
    /// </summary>
    public string TimeZoneId { get; set; } = "SE Asia Standard Time";
}
