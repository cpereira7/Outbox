namespace Outbox.Api.DTOs;

public record CreatePackageResponse(
    string TrackingCode,
    DateTimeOffset CreatedAt
);