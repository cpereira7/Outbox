using Microsoft.AspNetCore.Http.HttpResults;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Model;

namespace Outbox.Infrastructure.Service;

public class PackageEventService
{
    private readonly IPackageEventQueue _packageEventQueue;

    public PackageEventService(IPackageEventQueue packageEventQueue)
    {
        _packageEventQueue = packageEventQueue;
    }

    public async Task<bool> CreatePackageEventAsync(Package package, string? message, PackageStatus status)
    {
        var packageEvent = new PackageEvent
        (
            package.TrackingCode,
            PackageStatus.Created,
            package.CurrentHubId ?? package.ParcelShopId,
            message
        );

        return await _packageEventQueue.EnqueueAsync(packageEvent);
    }

    public async Task<bool> CreatePackageEventAsync(string trackingCode, string? message, PackageStatus status, Guid hubId)
    {
        var packageEvent = new PackageEvent
        (
            trackingCode,
            status,
            hubId,
            message
        );

        return await _packageEventQueue.EnqueueAsync(packageEvent);
    }
}