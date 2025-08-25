using Outbox.Model;

namespace Outbox.Api.DTOs;

public record UpdatePackageRequest(
    string TrackingCode,
    PackageStatus Status,
    Guid CurrentHubId,
    string? Message
);