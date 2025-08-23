namespace Outbox.Model;

public record PackageEvent(
    string TrackingCode,
    PackageStatus Status,
    Guid? Location,
    string? Message)
{
    DateTimeOffset CreatedAt => DateTimeOffset.UtcNow;
}
