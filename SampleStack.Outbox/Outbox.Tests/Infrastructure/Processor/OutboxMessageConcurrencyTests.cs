using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Model;
using Outbox.Service;

namespace Outbox.Tests.Infrastructure.Processor;

/// <summary>
/// Tests for PostgreSQL optimistic concurrency behavior using xmin with Testcontainers.
/// These tests verify that xmin-based concurrency control works correctly with real PostgreSQL.
/// </summary>
[Trait("Category", "Integration")]
public class OutboxMessageConcurrencyTests(PostgreSqlContainerFixture fixture) 
    : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private PackageDbContext _dbContext = null!;
    private IServiceProvider _serviceProvider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddDbContext<PackageDbContext>(options => 
            options.UseNpgsql(fixture.ConnectionString));
        
        _serviceProvider = services.BuildServiceProvider();
        
        var scope = _serviceProvider.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
        await _dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            (_serviceProvider as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task OutboxMessage_ShouldThrowConcurrencyException_WhenCompetingForSameMessage()
    {
        // Arrange: Create an outbox message
        var packageEvent = new PackageEvent(
            TrackingCode: "CTT-9Z-TEST-001",
            Status: PackageStatus.InTransit,
            Location: Guid.NewGuid(),
            Message: "Package in transit"
        );

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent.TrackingCode,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            IsCanceled = false
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();
        var messageId = outboxMessage.Id;

        // Act: Simulate two concurrent processors trying to process the same message
        // This tests the real optimistic concurrency behavior with xmin
        
        // Create two separate scopes (simulating two processor instances)
        using var scope1 = _serviceProvider.CreateScope();
        using var scope2 = _serviceProvider.CreateScope();

        var dbContext1 = scope1.ServiceProvider.GetRequiredService<PackageDbContext>();
        var dbContext2 = scope2.ServiceProvider.GetRequiredService<PackageDbContext>();

        // Both load the same message (with same xmin value)
        var message1 = await dbContext1.OutboxMessages.FindAsync(messageId);
        var message2 = await dbContext2.OutboxMessages.FindAsync(messageId);

        Assert.NotNull(message1);
        Assert.NotNull(message2);

        // First processor updates and commits
        message1.IsCompleted = true;
        message1.ProcessedAt = DateTimeOffset.UtcNow;
        await dbContext1.SaveChangesAsync(); // This succeeds, xmin is updated

        // Second processor tries to update with stale xmin
        message2.IsCompleted = true;
        message2.ProcessedAt = DateTimeOffset.UtcNow;

        // Assert: Second update should throw DbUpdateConcurrencyException
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            async () => await dbContext2.SaveChangesAsync()
        );

        Assert.NotNull(exception);

        // Verify the message is marked as completed by the first processor
        var finalMessage = await _dbContext.OutboxMessages
            .AsNoTracking()
            .FirstAsync(m => m.Id == messageId);
        Assert.NotNull(finalMessage);
        Assert.True(finalMessage.IsCompleted);
        Assert.NotNull(finalMessage.ProcessedAt);
    }

    [Fact]
    public async Task OutboxMessage_XminRowVersion_ShouldBePopulatedByPostgreSQL()
    {
        // Arrange: Create a message
        var packageEvent = new PackageEvent(
            TrackingCode: "CTT-9Z-TEST-XMIN",
            Status: PackageStatus.Created,
            Location: null,
            Message: "Testing xmin"
        );

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent.TrackingCode,
            Type = OutboxMessageType.Create,
            Payload = JsonSerializer.Serialize(packageEvent),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            IsCanceled = false
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();

        // Act: Reload to get the xmin value
        var savedMessage = await _dbContext.OutboxMessages
            .AsNoTracking()
            .FirstAsync(m => m.Id == outboxMessage.Id);

        // Assert: xmin (RowVersion) should be populated by PostgreSQL
        Assert.NotEqual(0u, savedMessage.RowVersion);

        // Update the message
        var messageToUpdate = await _dbContext.OutboxMessages.FindAsync(outboxMessage.Id);
        Assert.NotNull(messageToUpdate);
        
        var originalRowVersion = messageToUpdate.RowVersion;
        
        messageToUpdate.IsCompleted = true;
        messageToUpdate.ProcessedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Reload and verify xmin changed
        var updatedMessage = await _dbContext.OutboxMessages
            .AsNoTracking()
            .FirstAsync(m => m.Id == outboxMessage.Id);

        // xmin should be different after update (PostgreSQL increments it)
        Assert.NotEqual(originalRowVersion, updatedMessage.RowVersion);
    }
}

