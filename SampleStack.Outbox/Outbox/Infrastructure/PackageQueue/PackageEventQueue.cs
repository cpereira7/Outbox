using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Outbox.Infrastructure.Persistence;
using Outbox.Model;

namespace Outbox.Infrastructure.PackageQueue;

public class PackageEventQueue : IPackageEventQueue
{
    private readonly PackageDbContext _dbContext;
    private readonly ILogger<PackageEventQueue> _logger;

    public PackageEventQueue(PackageDbContext dbContext, ILogger<PackageEventQueue> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> EnqueueAsync(PackageEvent packageEvent)
    {
        var queueMessage = CreateOutboxMessage(packageEvent);

        try
        {
            _dbContext.OutboxMessages.Add(queueMessage);
            await _dbContext.SaveChangesAsync();
            
            _logger.LogInformation("Outbox message enqueued for {Tracking}", packageEvent.TrackingCode);
                
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send outbox message for {Tracking} with {Status}", 
                packageEvent.TrackingCode, packageEvent.Status);
            return false;
        }
    }

    private static OutboxMessage CreateOutboxMessage(PackageEvent packageEvent)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent.TrackingCode,
            Type = OutboxMessageType.Create,
            Payload = JsonSerializer.Serialize(packageEvent),
            OccurredAt = DateTimeOffset.Now
        };
    }

    public async Task<bool> TryDequeueAsync(string trackingCode)
    {
        var pendingUpdate = await _dbContext
            .OutboxMessages.Where(p => p.TrackingCode == trackingCode && !p.IsCanceled)
            .FirstOrDefaultAsync();
        
        if (pendingUpdate == null)
            return false;
        
        pendingUpdate.IsCanceled = true;
        pendingUpdate.ProcessedAt = DateTimeOffset.Now;
        
        await _dbContext.SaveChangesAsync();

        return true;
    }
}