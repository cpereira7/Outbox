using Microsoft.EntityFrameworkCore;
using Outbox.Infrastructure.Persistence;
using Outbox.Model;

namespace Outbox.Infrastructure.Repository;

public class PackageRepository : IPackageRepository
{
    private readonly PackageDbContext _dbContext;

    public PackageRepository(PackageDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Package?> GetByTrackingCodeAsync(string trackingCode)
    {
        return await _dbContext.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TrackingCode == trackingCode);
    }

    public async Task<Package?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public Task<Package> CreateAsync(Package package)
    {
        _dbContext.Packages.Add(package);
        return Task.FromResult(package);
    }

    public Task UpdateAsync(Package package)
    {
        _dbContext.Packages.Update(package);
        return Task.CompletedTask;
    }
}

