namespace Outbox.Api.DTOs;

public record CreatePackageRequest(
    Guid ParcelShopId,
    Guid SenderId,
    Guid OriginAddressId,
    Guid DestinationAddressId,
    decimal WeightKg
);