using Outbox.Model;

namespace Outbox.Infrastructure.Repository;

public interface IPackageRepository
{
    Task<Package?> GetByTrackingCodeAsync(string trackingCode);
    Task<Package?> GetByIdAsync(Guid id);
    Task<Package> CreateAsync(Package package);
    Task UpdateAsync(Package package);
}

