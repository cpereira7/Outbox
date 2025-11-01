namespace Outbox.Model;

public record PackageEvent(
    string TrackingCode,
    PackageStatus Status,
    Guid? Location,
    string? Message)
{
    private DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
