using Outbox.Model;

namespace Outbox.Infrastructure.PackageQueue;

public interface IPackageEventQueue
{
    void Enqueue(PackageEvent packageEvent, OutboxMessageType type);

    Task<bool> TryDequeueAsync(string trackingCode);
}