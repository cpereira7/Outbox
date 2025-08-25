using System.ComponentModel.DataAnnotations;

namespace Outbox.Model;

public record Package(
    string TrackingCode,
    Guid ParcelShopId,
    Guid SenderId,
    Guid OriginAddressId,
    Guid DestinationAddressId,
    decimal WeightKg)
{
    [Key] public Guid Id { get; init; }
    public PackageStatus CurrentStatus { get; init; } = PackageStatus.Created;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    
    public Guid? CurrentHubId { get; init; } 
    
    public PackageEvent ToCreatedEvent(string message = "Package created")
        => new PackageEvent(TrackingCode, PackageStatus.Created, ParcelShopId, message);
}

public enum PackageStatus
{
    Created,
    AwaitingPickup,
    InTransit,
    OutForDelivery,
    DeliveryAttempted,
    Delivered,
    ReturnedToSender,
    LostInTransit,
    Damaged
}