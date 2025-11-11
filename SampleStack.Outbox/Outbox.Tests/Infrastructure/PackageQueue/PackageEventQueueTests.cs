using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Model;
using Xunit;

namespace Outbox.Tests.Infrastructure.PackageQueue;

[Trait("Category", "Unit")]
public class PackageEventQueueTests : IDisposable
{
    private readonly PackageDbContext _dbContext;
    private readonly PackageEventQueue _eventQueue;

    public PackageEventQueueTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PackageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new PackageDbContext(options);

        // Setup mocks
        var mockLogger = Substitute.For<ILogger<PackageEventQueue>>();

        // Create system under test
        _eventQueue = new PackageEventQueue(_dbContext, mockLogger);
    }

    [Fact]
    public void Enqueue_ShouldAddOutboxMessageToDbContext_WhenCalled()
    {
        // Arrange
        var packageEvent = new PackageEvent(
            TrackingCode: "CTT-9Z-12345678",
            Status: PackageStatus.Created,
            Location: Guid.NewGuid(),
            Message: "Package created"
        );

        // Act
        _eventQueue.Enqueue(packageEvent, OutboxMessageType.Create);

        // Assert - check in ChangeTracker since SaveChanges hasn't been called
        var outboxMessage = _dbContext.ChangeTracker.Entries<OutboxMessage>()
            .Select(e => e.Entity)
            .FirstOrDefault();
        Assert.NotNull(outboxMessage);
        Assert.Equal(packageEvent.TrackingCode, outboxMessage.TrackingCode);
        Assert.Equal(OutboxMessageType.Create, outboxMessage.Type);
        Assert.Contains(packageEvent.TrackingCode, outboxMessage.Payload);
    }

    [Fact]
    public void Enqueue_ShouldCreateUniqueId_ForEachMessage()
    {
        // Arrange
        var packageEvent1 = new PackageEvent(
            TrackingCode: "CTT-9Z-11111111",
            Status: PackageStatus.Created,
            Location: Guid.NewGuid(),
            Message: "Package 1 created"
        );

        var packageEvent2 = new PackageEvent(
            TrackingCode: "CTT-9Z-22222222",
            Status: PackageStatus.Created,
            Location: Guid.NewGuid(),
            Message: "Package 2 created"
        );

        // Act
        _eventQueue.Enqueue(packageEvent1, OutboxMessageType.Create);
        _eventQueue.Enqueue(packageEvent2, OutboxMessageType.Create);

        // Assert - check in ChangeTracker since SaveChanges hasn't been called
        var messages = _dbContext.ChangeTracker.Entries<OutboxMessage>()
            .Select(e => e.Entity)
            .ToList();
        Assert.Equal(2, messages.Count);
        Assert.NotEqual(messages[0].Id, messages[1].Id);
    }

    [Fact]
    public void Enqueue_ShouldSetOccurredAt_WhenCalled()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;
        var packageEvent = new PackageEvent(
            TrackingCode: "CTT-9Z-12345678",
            Status: PackageStatus.Created,
            Location: Guid.NewGuid(),
            Message: "Package created"
        );

        // Act
        _eventQueue.Enqueue(packageEvent, OutboxMessageType.Create);
        var after = DateTimeOffset.UtcNow;

        // Assert - check in ChangeTracker since SaveChanges hasn't been called
        var outboxMessage = _dbContext.ChangeTracker.Entries<OutboxMessage>()
            .Select(e => e.Entity)
            .First();
        Assert.True(outboxMessage.OccurredAt >= before);
        Assert.True(outboxMessage.OccurredAt <= after);
    }

    [Fact]
    public void Enqueue_ShouldSerializePackageEvent_ToJson()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var location = Guid.NewGuid();
        var packageEvent = new PackageEvent(
            TrackingCode: trackingCode,
            Status: PackageStatus.InTransit,
            Location: location,
            Message: "In transit"
        );

        // Act
        _eventQueue.Enqueue(packageEvent, OutboxMessageType.Update);

        // Assert - check in ChangeTracker since SaveChanges hasn't been called
        var outboxMessage = _dbContext.ChangeTracker.Entries<OutboxMessage>()
            .Select(e => e.Entity)
            .First();
        Assert.Contains(trackingCode, outboxMessage.Payload);
        Assert.Contains("In transit", outboxMessage.Payload);
        Assert.Contains(location.ToString(), outboxMessage.Payload);
    }

    [Fact]
    public async Task TryDequeueAsync_ShouldMarkMessageAsCanceled_WhenMessageExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Create,
            Payload = "{\"TrackingCode\":\"" + trackingCode + "\"}",
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _eventQueue.TryDequeueAsync(trackingCode);

        // Assert
        Assert.True(result);
        var updatedMessage = await _dbContext.OutboxMessages.FindAsync(outboxMessage.Id);
        Assert.NotNull(updatedMessage);
        Assert.True(updatedMessage.IsCanceled);
        Assert.NotNull(updatedMessage.ProcessedAt);
    }

    [Fact]
    public async Task TryDequeueAsync_ShouldReturnFalse_WhenMessageNotFound()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-99999999";

        // Act
        var result = await _eventQueue.TryDequeueAsync(trackingCode);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryDequeueAsync_ShouldReturnFalse_WhenMessageAlreadyCanceled()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Create,
            Payload = "{\"TrackingCode\":\"" + trackingCode + "\"}",
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = true,
            IsCompleted = false
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _eventQueue.TryDequeueAsync(trackingCode);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryDequeueAsync_ShouldCancelFirstNonCanceledMessage_WhenMultipleExist()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var message1 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Create,
            Payload = "{}",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            IsCanceled = false,
            IsCompleted = false
        };

        var message2 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Update,
            Payload = "{}",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            IsCanceled = false,
            IsCompleted = false
        };

        _dbContext.OutboxMessages.AddRange(message1, message2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _eventQueue.TryDequeueAsync(trackingCode);

        // Assert
        Assert.True(result);
        var canceledCount = await _dbContext.OutboxMessages
            .CountAsync(m => m.TrackingCode == trackingCode && m.IsCanceled);
        Assert.Equal(1, canceledCount);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}

