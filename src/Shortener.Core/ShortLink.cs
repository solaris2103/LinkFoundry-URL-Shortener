namespace Shortener.Core;

public sealed class ShortLink
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string DestinationUrl { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ClickEvent> Clicks { get; set; } = [];
}

public sealed class ClickEvent
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required DateTime OccurredAt { get; set; }
    public string? ReferrerHost { get; set; }
    public required ShortLink Link { get; set; }
}

public sealed record CreateLinkRequest(string DestinationUrl, string? CustomCode, DateTimeOffset? ExpiresAt);

public sealed record LinkSummary(
    string Code,
    string ShortUrl,
    string DestinationUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    long ClickCount,
    bool IsActive);

public sealed record LinkAnalytics(
    LinkSummary Link,
    IReadOnlyList<DailyClickCount> DailyClicks,
    IReadOnlyList<ReferrerCount> TopReferrers);

public sealed record DailyClickCount(DateOnly Date, int Clicks);

public sealed record ReferrerCount(string Host, int Clicks);

public interface IShortLinkService
{
    Task<LinkSummary> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<LinkSummary>> ListAsync(CancellationToken cancellationToken);
    Task<LinkAnalytics?> GetAnalyticsAsync(string code, CancellationToken cancellationToken);
    Task<string?> RecordClickAsync(string code, string? referrer, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(string code, CancellationToken cancellationToken);
}