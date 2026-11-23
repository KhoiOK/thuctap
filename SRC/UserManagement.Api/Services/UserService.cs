using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UserManagement.Api.Data;
using UserManagement.Api.DTOs.Users;
using UserManagement.Api.Entities;
using UserManagement.Api.Exceptions;
using UserManagement.Api.FileStorage;
using UserManagement.Api.Validators;

namespace UserManagement.Api.Services;

/// <summary>
/// Default <see cref="IUserService"/> implementation. Uses <see cref="ApplicationDbContext"/>
/// with the configured global query filter, so soft-deleted users are excluded automatically.
/// </summary>
public class UserService : IUserService
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "fullName", "email", "createdAt"
    };

    private readonly ApplicationDbContext _db;
    private readonly ICloudinaryStorage _cloudinary;
    private readonly ILogger<UserService> _logger;
    private readonly TimeProvider _time;

    public UserService(
        ApplicationDbContext db,
        ICloudinaryStorage cloudinary,
        ILogger<UserService> logger,
        TimeProvider time)
    {
        _db = db;
        _cloudinary = cloudinary;
        _logger = logger;
        _time = time;
    }

    // ---------- API-01 List ----------

    public async Task<PagedResultDto<UserListItemDto>> GetPagedAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize, sortBy, sortOrder, keyword) = NormalizeQuery(query);

        // Base query (global filter hides soft-deleted users).
        IQueryable<User> q = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var norm = NormalizeKeyword(keyword);
            // SRS v2.0 §FR-02: a keyword that looks like a phone number may use the +84 /
            // 84 prefix. Normalize it to the local 0-prefixed form and search for that too.
            var phoneCandidates = PhoneSearchCandidates(keyword);

            // All alternatives must be OR-ed: a keyword may legitimately match a name,
            // an email or a phone number, and the phone-prefix variants are just extra
            // ways to match the phone column - never an additional AND-ed restriction.
            q = q.Where(u =>
                u.FullNameSearch.Contains(norm) ||
                u.Email.Contains(norm) ||
                u.PhoneNumber == norm ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(norm)) ||
                (u.PhoneNumber != null && phoneCandidates.Contains(u.PhoneNumber)) ||
                (u.PhoneNumber != null && phoneCandidates.Any(p => u.PhoneNumber.Contains(p))));
        }

        // Stable sort with Id as tie-breaker.
        q = (sortBy, sortOrder) switch
        {
            ("fullName", "asc") => q.OrderBy(u => u.FullName).ThenBy(u => u.Id),
            ("fullName", "desc") => q.OrderByDescending(u => u.FullName).ThenBy(u => u.Id),
            ("email", "asc") => q.OrderBy(u => u.Email).ThenBy(u => u.Id),
            ("email", "desc") => q.OrderByDescending(u => u.Email).ThenBy(u => u.Id),
            _ => q.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id) // default
        };

        var totalItems = await q.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        // Project to the list DTO - use projection so we don't load navigation collections.
        var rowsQuery =
            from u in q
            join d in _db.IdentityDocuments.AsNoTracking() on u.Id equals d.UserId into docs
            select new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.Gender,
                u.CreatedAt,
                DocumentCount = docs.Count()
            };

        var rows = await rowsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new UserListItemDto(
                r.Id, r.FullName, r.Email, r.PhoneNumber, r.Gender, r.DocumentCount, r.CreatedAt))
            .ToList();

        return new PagedResultDto<UserListItemDto>(items, page, pageSize, totalItems, totalPages);
    }

    private static (int page, int pageSize, string sortBy, string sortOrder, string keyword) NormalizeQuery(UserListQuery query)
    {
        var errors = new Dictionary<string, List<string>>();

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => query.PageSize
        };

        var sortBy = (query.SortBy ?? "createdAt").Trim();
        if (!AllowedSortFields.Contains(sortBy))
        {
            errors["sortBy"] = new List<string>
            {
                $"sortBy không hợp lệ. Chỉ chấp nhận: {string.Join(", ", AllowedSortFields)}."
            };
            sortBy = "createdAt";
        }

        var sortOrder = (query.SortOrder ?? "desc").Trim().ToLowerInvariant();
        if (sortOrder is not ("asc" or "desc"))
        {
            errors["sortOrder"] = new List<string> { "sortOrder chỉ chấp nhận 'asc' hoặc 'desc'." };
            sortOrder = "desc";
        }

        var keyword = (query.Keyword ?? string.Empty).Trim();
        if (keyword.Length > 100)
        {
            errors["keyword"] = new List<string> { "Từ khóa tối đa 100 ký tự." };
            keyword = keyword[..100];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }

        return (page, pageSize, sortBy, sortOrder, keyword);
    }

    private static string NormalizeKeyword(string keyword)
    {
        // For name search we use the lower-case no-diacritics form so the comparison matches
        // FullNameSearch semantics. For email/phone we use the raw lower-case form because the
        // underlying columns are already normalized.
        var lowered = keyword.ToLowerInvariant();
        return RemoveDiacritics(lowered);
    }

    /// <summary>
    /// Builds the phone-number variants that should be searched for a keyword. A keyword
    /// starting with <c>+84</c> (or with <c>84</c> followed by at least 5 digits) is
    /// converted to the local <c>0</c>-prefixed form, so <c>+84912345678</c> finds
    /// <c>0912345678</c> (SRS v2.0 §FR-02).
    /// </summary>
    private static List<string> PhoneSearchCandidates(string keyword)
    {
        var cleaned = new string(keyword
            .Where(ch => ch is not (' ' or '.' or '-'))
            .ToArray());

        if (cleaned.Length < 5)
        {
            return new List<string>();
        }

        var local = cleaned switch
        {
            _ when cleaned.StartsWith("+84", StringComparison.Ordinal) => "0" + cleaned[3..],
            _ when cleaned.StartsWith("84", StringComparison.Ordinal) && cleaned.Length >= 5 => "0" + cleaned[2..],
            _ => cleaned
        };

        var candidates = new List<string>();
        if (local.Length > 0)
        {
            candidates.Add(local);
        }

        return candidates;
    }

    private static string RemoveDiacritics(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString()
                 .Replace('đ', 'd')
                 .Replace('Đ', 'd')
                 .Normalize(NormalizationForm.FormC);
    }

    // ---------- API-02 Detail ----------

    public async Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.IdentityDocuments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("Người dùng không tồn tại hoặc đã bị xóa.");
        }

        return MapToDetail(user);
    }

    // ---------- API-03 Create ----------

    public async Task<UserDetailDto> CreateAsync(UserUpsertRequest request, CancellationToken cancellationToken)
    {
        var (fullName, email, phone) = UserValidator.ValidateAndNormalize(request);

        // Pre-check unique constraints to give a clear 409 instead of a generic one.
        await EnsureEmailAvailableAsync(email, excludeUserId: null, cancellationToken);
        await EnsurePhoneAvailableAsync(phone, excludeUserId: null, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            FullNameSearch = User.BuildFullNameSearch(fullName),
            Email = email,
            PhoneNumber = phone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            Address = request.Address,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert between our pre-check and the actual save - bubble up,
            // the global handler converts it to 409.
            throw;
        }

        return await GetByIdAsync(user.Id, cancellationToken);
    }

    // ---------- API-04 Update ----------

    public async Task<UserDetailDto> UpdateAsync(Guid id, UserUpsertRequest request, CancellationToken cancellationToken)
    {
        var (fullName, email, phone) = UserValidator.ValidateAndNormalize(request);

        var user = await _db.Users
            .Include(u => u.IdentityDocuments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("Người dùng không tồn tại hoặc đã bị xóa.");
        }

        await EnsureEmailAvailableAsync(email, excludeUserId: id, cancellationToken);
        await EnsurePhoneAvailableAsync(phone, excludeUserId: id, cancellationToken);

        user.FullName = fullName;
        user.FullNameSearch = User.BuildFullNameSearch(fullName);
        user.Email = email;
        user.PhoneNumber = phone;
        user.DateOfBirth = request.DateOfBirth;
        user.Gender = request.Gender;
        user.Address = request.Address;
        user.UpdatedAt = _time.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    // ---------- API-05 Soft delete ----------

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(u => u.IdentityDocuments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
        {
            return false;
        }

        // Collect the Cloudinary public ids to delete after the database commit.
        var imagePublicIds = user.IdentityDocuments
            .SelectMany(d => new[] { d.FrontImagePublicId, d.BackImagePublicId }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!))
            .ToList();

        user.IsDeleted = true;
        user.DeletedAt = _time.GetUtcNow().UtcDateTime;
        // Eagerly delete dependent identity documents (BR-05) - the relationship uses
        // ON DELETE RESTRICT in the database, so we remove the dependents here instead.
        _db.IdentityDocuments.RemoveRange(user.IdentityDocuments);

        // Remove the OCR scan history of this user (FK is NO_ACTION, so we must do it here).
        var ocrRecords = await _db.OcrExtractionResults
            .Where(o => o.UserId == id)
            .ToListAsync(cancellationToken);
        _db.OcrExtractionResults.RemoveRange(ocrRecords);

        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort asset cleanup. Failures are logged but do not fail the operation (BR-05).
        foreach (var publicId in imagePublicIds)
        {
            try
            {
                var failed = await _cloudinary.DeleteAsync(new[] { publicId }, CancellationToken.None);
                if (failed.Count > 0)
                {
                    _logger.LogWarning("Cloudinary asset {PublicId} could not be deleted while soft-deleting user {UserId}.",
                        publicId, id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Cloudinary asset {PublicId} while soft-deleting user {UserId}.", publicId, id);
            }
        }

        return true;
    }

    // ---------- Helpers ----------

    private async Task EnsureEmailAvailableAsync(string email, Guid? excludeUserId, CancellationToken cancellationToken)
    {
        // BR-01: uniqueness applies to ACTIVE (not soft-deleted) users only.
        // The default global query filter already excludes soft-deleted rows, so we just
        // need to additionally exclude the current user when updating.
        var query = _db.Users.Where(u => u.Email == email);
        if (excludeUserId is not null)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        var exists = await query.AnyAsync(cancellationToken);
        if (exists)
        {
            throw new ConflictException("email", "Email đã được sử dụng bởi người dùng khác.");
        }
    }

    private async Task EnsurePhoneAvailableAsync(string phone, Guid? excludeUserId, CancellationToken cancellationToken)
    {
        // BR-02: uniqueness applies to ACTIVE (not soft-deleted) users only.
        var query = _db.Users.Where(u => u.PhoneNumber == phone);
        if (excludeUserId is not null)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        var exists = await query.AnyAsync(cancellationToken);
        if (exists)
        {
            throw new ConflictException("phoneNumber", "Số điện thoại đã được sử dụng bởi người dùng khác.");
        }
    }

    private static UserDetailDto MapToDetail(User user)
    {
        var docs = user.IdentityDocuments
            .OrderBy(d => d.DocumentType)
            .Select(d => new IdentityDocumentListItemDto(
                d.Id,
                d.DocumentType,
                d.DocumentNumber,
                d.IssuedDate,
                d.ExpiryDate,
                !string.IsNullOrWhiteSpace(d.BackImagePublicId),
                FrontImageUrl: BuildImageUrl(user.Id, d.Id, "front"),
                BackImageUrl: string.IsNullOrWhiteSpace(d.BackImagePublicId) ? null : BuildImageUrl(user.Id, d.Id, "back"),
                d.CreatedAt,
                d.UpdatedAt))
            .ToList();

        return new UserDetailDto(
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.DateOfBirth,
            user.Gender,
            user.Address,
            user.CreatedAt,
            user.UpdatedAt,
            docs);
    }

    private static string BuildImageUrl(Guid userId, Guid documentId, string side)
    {
        // Relative URL only - never expose the physical file path. The frontend proxies this
        // through its own controller that calls API-10. We keep the contract from API-Spec.
        return $"/api/v1/users/{userId}/identity-documents/{documentId}/images/{side}";
    }
}
