using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Model;
using Outbox.Service;
 
namespace Outbox.Infrastructure.Processor;

public class PackageEventQueueProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PackageEventQueueProcessor> _logger;

    public PackageEventQueueProcessor(IServiceScopeFactory scopeFactory, ILogger<PackageEventQueueProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delayInterval = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();

            try
            {
                var eventsInQueue = await dbContext.OutboxMessages
                    .Where(e => !e.IsCanceled && !e.IsCompleted)
                    //.OrderByDescending(e => e.OccurredAt)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                if (eventsInQueue.Count == 0)
                {
                    await Task.Delay(delayInterval, stoppingToken);
                    continue;
                }
                
                foreach (var outboxMessage in eventsInQueue)
                {
                    await ProcessOutboxMessage(outboxMessage, dbContext, scope, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred processing Outbox Messages");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessOutboxMessage(OutboxMessage outboxMessage, PackageDbContext dbContext, IServiceScope scope, CancellationToken stoppingToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);

        try
        {
            // Reload the message for update with optimistic concurrency
            var messageToProcess = await dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Id == outboxMessage.Id, stoppingToken);

            if (messageToProcess == null || messageToProcess.IsCompleted)
            {
                await transaction.CommitAsync(stoppingToken);
                return;
            }

            // Deserialize the package event
            var packageEvent = JsonSerializer.Deserialize<PackageEvent>(messageToProcess.Payload) 
                               ?? throw new JsonException("Deserialization returned null");

            // Send a package update notification
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notificationService.SendPackageUpdateNotificationAsync(
                packageEvent.TrackingCode,
                packageEvent.Status,
                packageEvent.Message
            );

            // Mark as completed
            messageToProcess.IsCompleted = true;
            messageToProcess.ProcessedAt = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);

            _logger.LogInformation("Processed outbox message '{Id}' for package '{TrackingCode}'", 
                messageToProcess.Id, packageEvent.TrackingCode);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(stoppingToken);
            _logger.LogWarning(ex, "Concurrency exception. The PackageEvent '{Id}' might have been already cancelled/processed.", outboxMessage.Id);
        }
        catch (JsonException ex)
        {
            await CancelMessageAsync(outboxMessage, dbContext, transaction, stoppingToken);
            _logger.LogError(ex, "JSON error processing the message '{Id}'", outboxMessage.Id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(stoppingToken);
            _logger.LogError(ex, "Error processing the message '{Id}'", outboxMessage.Id);
        }
    }

    private static async Task CancelMessageAsync(OutboxMessage message, PackageDbContext dbContext, 
        IDbContextTransaction transaction, CancellationToken stoppingToken)
    {
        message.IsCanceled = true;
        message.ProcessedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken);
        await transaction.CommitAsync(stoppingToken);
    }
}