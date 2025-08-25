using Microsoft.EntityFrameworkCore;
using Outbox.Api.DTOs;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Service;
using Outbox.Model;

namespace Outbox.Service;

public class PackageManager
{
    private readonly PackageDbContext _dbContext;
    private readonly PackageEventService _eventService;

    public PackageManager(PackageDbContext dbContext, PackageEventService eventService)
    {
        _dbContext = dbContext;
        _eventService = eventService;
    }

    public async Task<CreatePackageResponse?> CreatePackageAsync(CreatePackageRequest request)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        var package = new Package(
            GenerateTrackingCode(),
            request.ParcelShopId,
            request.SenderId,
            request.OriginAddressId,
            request.DestinationAddressId,
            request.WeightKg
        );

        _dbContext.Packages.Add(package);
        await _dbContext.SaveChangesAsync();

        var createdEvent = package.ToCreatedEvent();
        
        var eventSent = await _eventService.RegisterPackageEvent(createdEvent);
        
        if (!eventSent)
        {
            await transaction.RollbackAsync();
            return null;
        }

        await transaction.CommitAsync();
        return new CreatePackageResponse(package.TrackingCode, package.CreatedAt);
    }

    public async Task<UpdatePackageResponse?> UpdatePackageAsync(UpdatePackageRequest request)
    {
        var package = await _dbContext.Packages
            .FirstOrDefaultAsync(p => p.TrackingCode == request.TrackingCode);

        if (package == null)
            return null;

        var enqueued = await _eventService.RegisterPackageEvent(
            request.TrackingCode, 
            request.Message, 
            request.Status, 
            request.CurrentHubId);

        return new UpdatePackageResponse(
            request.TrackingCode,
            request.Status,
            enqueued,
            DateTimeOffset.UtcNow
        );
    }
    
    public async Task<Package?> GetPackageByTrackingCodeAsync(string trackingCode)
    {
        return await _dbContext.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TrackingCode == trackingCode);
    }
    
    private string GenerateTrackingCode() => $"CTT-9Z-{Random.Shared.Next(1000000000, 1999999999)}";
}