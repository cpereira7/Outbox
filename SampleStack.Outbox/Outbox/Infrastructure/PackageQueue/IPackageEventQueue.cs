using Outbox.Model;

namespace Outbox.Infrastructure.PackageQueue;

public interface IPackageEventQueue
{
    Task<bool> EnqueueAsync(PackageEvent packageEvent);
    
    Task<bool> TryDequeueAsync(string trackingCode);
}