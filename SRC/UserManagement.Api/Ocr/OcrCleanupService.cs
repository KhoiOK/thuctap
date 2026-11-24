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

    /// <summary>
    /// Removes the temporary images of every scan whose expiry date has passed and
    /// marks the corresponding records as <see cref="OcrScanStatus.Expired"/>.
    ///
    /// A record is only marked as expired when ALL of its assets were deleted
    /// successfully. If a delete fails, the record keeps its expiry date so the next
    /// run of the job retries it - marking it as expired while the image is still on
    /// the CDN would leak the image forever (BR-16).
    /// </summary>
    internal async Task CleanupExpiredScansAsync(CancellationToken cancellationToken)
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

        // Assets that could not be deleted. The records that reference them keep their
        // expiry date so the next run of this job retries them - marking them as
        // "Expired" while the image is still on the CDN would leak the image forever
        // (BR-16: a failed delete must never be silently forgotten).
        var failedIds = new HashSet<string>(StringComparer.Ordinal);

        if (publicIds.Count > 0)
        {
            var failed = await cloudinary.DeleteAsync(publicIds, cancellationToken);
            foreach (var id in failed)
            {
                _logger.LogWarning("Expired Cloudinary asset {PublicId} could not be deleted.", id);
                failedIds.Add(id);
            }
        }

        var expiredNow = 0;
        foreach (var record in expired)
        {
            var recordIds = new[] { record.FrontImagePublicId, record.BackImagePublicId }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToList();

            if (recordIds.Any(id => failedIds.Contains(id)))
            {
                // Keep the record and its ExpiresAt so the next run retries the delete.
                continue;
            }

            record.Status = OcrScanStatus.Expired;
            record.ExpiresAt = null;
            expiredNow++;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Cleaned up {Count} expired OCR scan(s); {Pending} still pending because their assets could not be deleted.",
            expiredNow,
            expired.Count - expiredNow);
    }
}
