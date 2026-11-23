using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UserManagement.Api.Data;
using UserManagement.Api.Entities;
using UserManagement.Api.FileStorage;

namespace UserManagement.Api.Ocr;

/// <summary>
/// Background job that removes the temporary OCR images once they expire (BR-15).
/// Runs on a fixed interval and marks the corresponding scan records as
/// <see cref="OcrScanStatus.Expired"/>.
/// </summary>
public class OcrCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<OcrCleanupService> _logger;

    public OcrCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CloudinaryOptions> options,
        ILogger<OcrCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the application time to finish starting up.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredScansAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The OCR cleanup job failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupExpiredScansAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cloudinary = scope.ServiceProvider.GetRequiredService<ICloudinaryStorage>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var now = time.GetUtcNow().UtcDateTime;

        var expired = await db.OcrExtractionResults
            .Where(o => (o.Status == OcrScanStatus.Completed || o.Status == OcrScanStatus.Failed)
                        && o.ExpiresAt != null
                        && o.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        var publicIds = expired
            .SelectMany(o => new[] { o.FrontImagePublicId, o.BackImagePublicId }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!))
            .Distinct()
            .ToList();

        if (publicIds.Count > 0)
        {
            var failed = await cloudinary.DeleteAsync(publicIds, cancellationToken);
            foreach (var id in failed)
            {
                _logger.LogWarning("Expired Cloudinary asset {PublicId} could not be deleted.", id);
            }
        }

        foreach (var record in expired)
        {
            record.Status = OcrScanStatus.Expired;
            record.ExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleaned up {Count} expired OCR scan(s).", expired.Count);
    }
}
