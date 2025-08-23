using System.ComponentModel.DataAnnotations;

namespace Outbox.Infrastructure.PackageQueue;

public record OutboxMessage
{
    [Key]
    public Guid Id { get; init; }
    public required string TrackingCode { get; init; }
    public OutboxMessageType Type { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    
    public bool IsCanceled { get; set; } = false;
    public bool IsCompleted { get; set; } = false;
}

public enum OutboxMessageType
{
    Create,
    Update
}