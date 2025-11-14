using System.ComponentModel.DataAnnotations;

namespace Outbox.Api.DTOs;

public record CreatePackageRequest
{
    public required Guid ParcelShopId { get; init; }
    public required Guid SenderId { get; init; }
    public required Guid OriginAddressId { get; init; }
    public required Guid DestinationAddressId { get; init; }
    
    [Range(0.01, double.MaxValue, ErrorMessage = "Weight must be greater than zero")]
    public required decimal WeightKg { get; init; }
}