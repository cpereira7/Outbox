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

    public void Enqueue(PackageEvent packageEvent, OutboxMessageType type)
    {
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent.TrackingCode,
            Type = type,
            Payload = JsonSerializer.Serialize(packageEvent),
            OccurredAt = DateTimeOffset.UtcNow
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        _logger.LogInformation("Outbox message queued: {Type} for {Tracking}", 
            type, packageEvent.TrackingCode);
    }

    public async Task<bool> TryDequeueAsync(string trackingCode)
    {
        var pendingUpdate = await _dbContext
            .OutboxMessages.Where(p => p.TrackingCode == trackingCode && !p.IsCanceled)
            .FirstOrDefaultAsync();
        
        if (pendingUpdate == null)
            return false;
        
        pendingUpdate.IsCanceled = true;
        pendingUpdate.ProcessedAt = DateTimeOffset.UtcNow;
        
        await _dbContext.SaveChangesAsync();

        return true;
    }
}