using Outbox.Model;

namespace Outbox.Api.DTOs;

public record UpdatePackageRequest(
    PackageStatus Status,
    Guid CurrentHubId,
    string? Message
);