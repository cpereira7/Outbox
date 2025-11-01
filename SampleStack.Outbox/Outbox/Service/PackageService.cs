using Outbox.Api.DTOs;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Repository;
using Outbox.Model;

namespace Outbox.Service;

public class PackageService : IPackageService
{
    private readonly PackageDbContext _dbContext;
    private readonly IPackageRepository _packageRepository;
    private readonly IPackageEventQueue _outboxQueue;
    private readonly ILogger<PackageService> _logger;

    public PackageService(
        PackageDbContext dbContext,
        IPackageRepository packageRepository,
        IPackageEventQueue outboxQueue,
        ILogger<PackageService> logger)
    {
        _dbContext = dbContext;
        _packageRepository = packageRepository;
        _outboxQueue = outboxQueue;
        _logger = logger;
    }

    public async Task<CreatePackageResponse?> CreatePackageAsync(CreatePackageRequest request)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        try
        {
            // 1. Create package entity
            var trackingCode = GenerateTrackingCode();
            var package = Package.Create(request, trackingCode);
            
            // 2. Add to repository (doesn't save yet)
            await _packageRepository.CreateAsync(package);
            
            // 3. Queue outbox message (doesn't save yet)
            var packageEvent = package.ToCreatedEvent();
            _outboxQueue.Enqueue(packageEvent, OutboxMessageType.Create);
            
            // 4. Single atomic save for both Package + OutboxMessage
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            
            _logger.LogInformation("Package created: {Tracking}", trackingCode);
            return new CreatePackageResponse(package.TrackingCode, package.CreatedAt);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create package");
            return null;
        }
    }

    public async Task<UpdatePackageResponse?> UpdatePackageStatusAsync(UpdatePackageRequest request)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        
        try
        {
            // 1. Get and update package
            var package = await _packageRepository.GetByTrackingCodeAsync(request.TrackingCode);
            if (package == null)
                return null;

            // 2. Update package with new status
            var updatedPackage = package with 
            { 
                CurrentStatus = request.Status,
                CurrentHubId = request.CurrentHubId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            
            await _packageRepository.UpdateAsync(updatedPackage);
            
            // 3. Queue the update event
            var updateEvent = new PackageEvent(
                request.TrackingCode,
                request.Status,
                request.CurrentHubId,
                request.Message
            );
            
            _outboxQueue.Enqueue(updateEvent, OutboxMessageType.Update);
            
            // 4. Atomic save for both Package update + OutboxMessage
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            
            _logger.LogInformation("Package updated: {Tracking} to {Status}", 
                request.TrackingCode, request.Status);
            
            return new UpdatePackageResponse(
                request.TrackingCode,
                request.Status,
                true,
                DateTimeOffset.UtcNow
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to update package {Tracking}", request.TrackingCode);
            return null;
        }
    }
    
    public async Task<Package?> GetPackageByTrackingCodeAsync(string trackingCode)
    {
        return await _packageRepository.GetByTrackingCodeAsync(trackingCode);
    }
    
    private string GenerateTrackingCode() => 
        $"CTT-9Z-{Random.Shared.Next(1000000, 1999999999)}";
}