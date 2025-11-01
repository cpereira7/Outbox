using System.ComponentModel.DataAnnotations;

namespace Outbox.Model;

public record PackageEventHistory
{
    [Key] 
    public Guid Id { get; init; }

    public string? TrackingCode { get; init; }
    public PackageStatus Status { get; init; }
    public Guid? Location { get; init; }
    public string? Message { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    public Guid PackageId { get; init; }
    public Package? Package { get; init; }
}
