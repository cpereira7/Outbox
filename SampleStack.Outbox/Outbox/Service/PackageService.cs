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
            // Create package entity
            var trackingCode = GenerateTrackingCode();
            var package = Package.Create(request, trackingCode);
            
            await _packageRepository.CreateAsync(package);
            
            // Queue outbox message
            var packageEvent = package.ToCreatedEvent();
            _outboxQueue.Enqueue(packageEvent, OutboxMessageType.Create);
            
            // Save both package and outbox message
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
            // Update package status
            var package = await _packageRepository.GetByTrackingCodeAsync(request.TrackingCode);
            if (package == null)
                return null;
            
            var updatedPackage = package with 
            { 
                CurrentStatus = request.Status,
                CurrentHubId = request.CurrentHubId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            
            await _packageRepository.UpdateAsync(updatedPackage);
            
            // Queue outbox message
            var updateEvent = new PackageEvent(
                request.TrackingCode,
                request.Status,
                request.CurrentHubId,
                request.Message
            );
            
            _outboxQueue.Enqueue(updateEvent, OutboxMessageType.Update);
            
            // Commit the package update and outbox message
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
    
    private static string GenerateTrackingCode() => 
        $"CTT-9Z-{Random.Shared.Next(1000000, 1999999999)}";
}