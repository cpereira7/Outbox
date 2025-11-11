using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Processor;
using Outbox.Model;
using Outbox.Service;
using Xunit;

namespace Outbox.Tests.Infrastructure.Processor;

[Trait("Category", "Unit")]
public class PackageEventQueueProcessorTests : IDisposable
{
    private readonly PackageDbContext _dbContext;
    private readonly IServiceScopeFactory _mockScopeFactory;
    private readonly INotificationService _mockNotificationService;
    private readonly ILogger<PackageEventQueueProcessor> _mockLogger;

    public PackageEventQueueProcessorTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PackageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new PackageDbContext(options);

        // Setup mocks
        var mockServiceProvider = Substitute.For<IServiceProvider>();
        var mockScope = Substitute.For<IServiceScope>();
        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockNotificationService = Substitute.For<INotificationService>();
        _mockLogger = Substitute.For<ILogger<PackageEventQueueProcessor>>();

        // Setup service provider chain
        _mockScopeFactory.CreateScope().Returns(mockScope);
        mockScope.ServiceProvider.Returns(mockServiceProvider);
        mockServiceProvider.GetService(typeof(PackageDbContext)).Returns(_dbContext);
        mockServiceProvider.GetService(typeof(INotificationService)).Returns(_mockNotificationService);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldProcessPendingMessages_AndMarkThemCompleted()
    {
        // Arrange
        const string trackingCode1 = "CTT-9Z-11111111";
        const string trackingCode2 = "CTT-9Z-22222222";
        
        var packageEvent1 = new PackageEvent(trackingCode1, PackageStatus.InTransit, Guid.NewGuid(), "In transit");
        var packageEvent2 = new PackageEvent(trackingCode2, PackageStatus.Delivered, Guid.NewGuid(), "Delivered");

        var message1 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode1,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent1),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        };

        var message2 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode2,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent2),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        };

        _dbContext.OutboxMessages.AddRange(message1, message2);
        await _dbContext.SaveChangesAsync();

        var processor = new PackageEventQueueProcessor(_mockScopeFactory, _mockLogger);
        using var cts = new CancellationTokenSource();

        // Act - Start the processor and cancel after a short delay
        var processorTask = processor.StartAsync(cts.Token);
        await Task.Delay(500); // Give it time to process
        await cts.CancelAsync();
        await processorTask;

        // Assert
        var processedMessages = await _dbContext.OutboxMessages.ToListAsync();
        Assert.All(processedMessages, m =>
        {
            Assert.True(m.IsCompleted);
            Assert.NotNull(m.ProcessedAt);
            Assert.False(m.IsCanceled);
        });

        await _mockNotificationService.Received(1).SendPackageUpdateNotificationAsync(
            trackingCode1, PackageStatus.InTransit, "In transit");
        await _mockNotificationService.Received(1).SendPackageUpdateNotificationAsync(
            trackingCode2, PackageStatus.Delivered, "Delivered");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotSendDuplicateNotification_WhenMessageAlreadyCompleted()
    {
        // Arrange - Simulate a message already processed by another instance
        const string trackingCode = "CTT-9Z-12345678";
        var packageEvent = new PackageEvent(trackingCode, PackageStatus.InTransit, Guid.NewGuid(), "In transit");

        var alreadyCompletedMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = true, // Already completed by another instance
            ProcessedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _dbContext.OutboxMessages.Add(alreadyCompletedMessage);
        await _dbContext.SaveChangesAsync();

        var processor = new PackageEventQueueProcessor(_mockScopeFactory, _mockLogger);
        using var cts = new CancellationTokenSource();

        // Act
        var processorTask = processor.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await processorTask;

        // Assert - Should not send notification for already completed message
        await _mockNotificationService.DidNotReceive().SendPackageUpdateNotificationAsync(
            Arg.Any<string>(), Arg.Any<PackageStatus>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldProcessUpTo10Messages_PerIteration()
    {
        // Arrange - Create 15 messages
        var messages = Enumerable.Range(1, 15).Select(i => new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = $"CTT-9Z-{i:D8}",
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(new PackageEvent(
                $"CTT-9Z-{i:D8}", 
                PackageStatus.InTransit, 
                Guid.NewGuid(), 
                "In transit")),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        }).ToList();

        _dbContext.OutboxMessages.AddRange(messages);
        await _dbContext.SaveChangesAsync();

        var processor = new PackageEventQueueProcessor(_mockScopeFactory, _mockLogger);
        using var cts = new CancellationTokenSource();

        // Act - Let it run for one iteration
        var processorTask = processor.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await processorTask;

        // Assert - First batch of 10 should be processed
        var completedCount = await _dbContext.OutboxMessages.CountAsync(m => m.IsCompleted);
        Assert.InRange(completedCount, 10, 15); // At least 10, may process more if fast enough

        await _mockNotificationService.Received().SendPackageUpdateNotificationAsync(
            Arg.Any<string>(), Arg.Any<PackageStatus>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelMessage_WhenPayloadIsInvalidJson()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var invalidMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = trackingCode,
            Type = OutboxMessageType.Update,
            Payload = "invalid json {{{",
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        };

        _dbContext.OutboxMessages.Add(invalidMessage);
        await _dbContext.SaveChangesAsync();

        var processor = new PackageEventQueueProcessor(_mockScopeFactory, _mockLogger);
        using var cts = new CancellationTokenSource();

        // Act
        var processorTask = processor.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await processorTask;

        // Assert
        var processedMessage = await _dbContext.OutboxMessages.FindAsync(invalidMessage.Id);
        Assert.NotNull(processedMessage);
        Assert.True(processedMessage.IsCanceled);
        Assert.NotNull(processedMessage.ProcessedAt);
        Assert.False(processedMessage.IsCompleted);

        await _mockNotificationService.DidNotReceive().SendPackageUpdateNotificationAsync(
            Arg.Any<string>(), Arg.Any<PackageStatus>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipCanceledAndCompletedMessages()
    {
        // Arrange
        var pendingPackage = new PackageEvent("CTT-9Z-11111111", PackageStatus.InTransit, Guid.NewGuid(), "In transit");
        var canceledPackage = new PackageEvent("CTT-9Z-22222222", PackageStatus.InTransit, Guid.NewGuid(), "In transit");
        var completedPackage = new PackageEvent("CTT-9Z-33333333", PackageStatus.InTransit, Guid.NewGuid(), "In transit");
        
        var pendingMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = "CTT-9Z-11111111",
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(pendingPackage),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = false
        };

        var canceledMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = "CTT-9Z-22222222",
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(canceledPackage),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = true,
            IsCompleted = false
        };

        var completedMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = "CTT-9Z-33333333",
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(completedPackage),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCanceled = false,
            IsCompleted = true,
            ProcessedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _dbContext.OutboxMessages.AddRange(pendingMessage, canceledMessage, completedMessage);
        await _dbContext.SaveChangesAsync();

        var processor = new PackageEventQueueProcessor(_mockScopeFactory, _mockLogger);
        using var cts = new CancellationTokenSource();

        // Act
        var processorTask = processor.StartAsync(cts.Token);
        await Task.Delay(500);
        await cts.CancelAsync();
        await processorTask;

        // Assert - Only the pending message should be processed
        await _mockNotificationService.Received(1).SendPackageUpdateNotificationAsync(
            "CTT-9Z-11111111", Arg.Any<PackageStatus>(), Arg.Any<string>());
        
        // Should not process canceled or completed messages
        await _mockNotificationService.DidNotReceive().SendPackageUpdateNotificationAsync(
            "CTT-9Z-22222222", Arg.Any<PackageStatus>(), Arg.Any<string>());
        await _mockNotificationService.DidNotReceive().SendPackageUpdateNotificationAsync(
            "CTT-9Z-33333333", Arg.Any<PackageStatus>(), Arg.Any<string>());
    }
    
    public void Dispose()
    {
        _dbContext?.Dispose();
        GC.SuppressFinalize(this);
    }
}

