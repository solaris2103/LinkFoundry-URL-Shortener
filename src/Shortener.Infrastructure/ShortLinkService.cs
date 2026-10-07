using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Shortener.Core;

namespace Shortener.Infrastructure;

public sealed partial class ShortLinkService(ShortenerDbContext database, IConfiguration configuration) : IShortLinkService
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly HashSet<string> ReservedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "health", "swagger"
    };
    private static readonly TimeSpan AnalyticsWindow = TimeSpan.FromDays(30);

    public async Task<LinkSummary> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.DestinationUrl, UriKind.Absolute, out var destination) ||
            (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps) ||
            request.DestinationUrl.Length > 2048 ||
            !string.IsNullOrEmpty(destination.UserInfo) ||
            destination.AbsoluteUri.Length > 2048)
        {
            throw new ArgumentException("DestinationUrl must be a valid HTTP or HTTPS URL without embedded credentials and at most 2048 characters.");
        }

        if (request.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("ExpiresAt must be in the future.");
        }

        var code = request.CustomCode?.Trim();
        if (code is not null && !CustomCodePattern().IsMatch(code))
        {
            throw new ArgumentException("CustomCode must be 3-32 characters using letters, numbers, '-' or '_'.");
        }

        if (code is not null && ReservedCodes.Contains(code))
        {
            throw new ArgumentException("CustomCode is reserved for a service endpoint.");
        }

        if (code is not null)
        {
            var existingLink = await database.Links.SingleOrDefaultAsync(link => link.Code == code, cancellationToken);
            if (existingLink is not null)
            {
                if (existingLink.ExpiresAt is not { } existingExpiry || existingExpiry > DateTime.UtcNow)
                {
                    throw new InvalidOperationException("That short code is already in use.");
                }

                await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await database.Clicks
                        .Where(click => click.Code == code)
                        .ExecuteDeleteAsync(cancellationToken);
                    existingLink.DestinationUrl = destination.AbsoluteUri;
                    existingLink.CreatedAt = DateTime.UtcNow;
                    existingLink.ExpiresAt = request.ExpiresAt?.UtcDateTime;
                    existingLink.IsActive = true;
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return ToSummary(existingLink, 0);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
        }

        var customCode = code;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidateCode = customCode ?? await GenerateUniqueCodeAsync(cancellationToken);
            var link = new ShortLink
            {
                Code = candidateCode,
                DestinationUrl = destination.AbsoluteUri,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt?.UtcDateTime,
                IsActive = true
            };

            database.Links.Add(link);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return ToSummary(link, 0);
            }
            catch (DbUpdateException exception) when (IsCodeUniquenessViolation(exception))
            {
                database.Entry(link).State = EntityState.Detached;
                if (customCode is not null)
                {
                    throw new InvalidOperationException("That short code is already in use.");
                }
            }
        }

        throw new InvalidOperationException("Could not allocate a unique short code. Please retry.");
    }

    public async Task<IReadOnlyList<LinkSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var links = await database.Links.AsNoTracking()
            .OrderByDescending(link => link.CreatedAt)
            .Take(100)
            .Select(link => new { Link = link, ClickCount = link.Clicks.LongCount() })
            .ToListAsync(cancellationToken);
        return links.Select(item => ToSummary(item.Link, item.ClickCount)).ToArray();
    }

    public async Task<LinkAnalytics?> GetAnalyticsAsync(string code, CancellationToken cancellationToken)
    {
        var link = await database.Links.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (link is null)
        {
            return null;
        }

        var totalClicks = await database.Clicks.LongCountAsync(click => click.Code == code, cancellationToken);
        var since = DateTime.UtcNow.Subtract(AnalyticsWindow);
        var clicks = await database.Clicks.AsNoTracking()
            .Where(click => click.Code == code && click.OccurredAt >= since)
            .ToListAsync(cancellationToken);
        var daily = clicks.GroupBy(click => DateOnly.FromDateTime(click.OccurredAt))
            .OrderBy(group => group.Key)
            .Select(group => new DailyClickCount(group.Key, group.Count()))
            .ToArray();
        var referrers = clicks.Where(click => click.ReferrerHost is not null)
            .GroupBy(click => click.ReferrerHost!)
            .OrderByDescending(group => group.Count())
            .Take(5)
            .Select(group => new ReferrerCount(group.Key, group.Count()))
            .ToArray();

        return new LinkAnalytics(ToSummary(link, totalClicks), daily, referrers);
    }

    public async Task<string?> RecordClickAsync(string code, string? referrer, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var link = await database.Links.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (link is null || !link.IsActive || (link.ExpiresAt is { } expiresAt && expiresAt <= now))
        {
            return null;
        }

        string? referrerHost = null;
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var referrerUri) &&
            (referrerUri.Scheme == Uri.UriSchemeHttp || referrerUri.Scheme == Uri.UriSchemeHttps))
        {
            referrerHost = referrerUri.Host;
        }

        database.Clicks.Add(new ClickEvent
        {
            Code = code,
            OccurredAt = now,
            ReferrerHost = referrerHost,
            Link = link
        });
        await database.SaveChangesAsync(cancellationToken);
        return link.DestinationUrl;
    }

    public async Task<bool> DeactivateAsync(string code, CancellationToken cancellationToken)
    {
        var link = await database.Links.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (link is null)
        {
            return false;
        }

        link.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            var candidate = new string(bytes.Take(12).Select(value => Alphabet[value % Alphabet.Length]).ToArray());
            if (!await database.Links.AnyAsync(link => link.Code == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique short code. Please retry.");
    }

    private LinkSummary ToSummary(ShortLink link, long clickCount)
    {
        var baseUrl = configuration["PublicBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5080";
        var isActive = link.IsActive && (link.ExpiresAt is null || link.ExpiresAt > DateTime.UtcNow);
        return new LinkSummary(link.Code, $"{baseUrl}/{link.Code}", link.DestinationUrl,
            link.CreatedAt, link.ExpiresAt, clickCount, isActive);
    }

    private static bool IsCodeUniquenessViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex CustomCodePattern();
}