using Outbox.Model;

namespace Outbox.Api.DTOs;

public record UpdatePackageResponse(
    string TrackingCode,
    PackageStatus RequestedStatus,
    bool Enqueued,
    DateTimeOffset EnqueuedAt
);
