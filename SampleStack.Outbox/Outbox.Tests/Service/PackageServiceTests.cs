using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Outbox.Api.DTOs;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Repository;
using Outbox.Model;
using Outbox.Service;
using Xunit;

namespace Outbox.Tests.Service;

[Trait("Category", "Unit")]
public class PackageServiceTests : IDisposable
{
    private readonly PackageDbContext _dbContext;
    private readonly IPackageRepository _mockRepository;
    private readonly IPackageEventQueue _mockQueue;
    private readonly PackageService _packageService;

    public PackageServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PackageDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new PackageDbContext(options);

        // Setup mocks
        _mockRepository = Substitute.For<IPackageRepository>();
        _mockQueue = Substitute.For<IPackageEventQueue>();
        
        // Configure mock queue to add outbox messages to DbContext when Enqueue is called
        // This simulates the real behavior for transaction commit tests
        _mockQueue.When(x => x.Enqueue(Arg.Any<PackageEvent>(), Arg.Any<OutboxMessageType>()))
            .Do(callInfo =>
            {
                var packageEvent = callInfo.Arg<PackageEvent>();
                var type = callInfo.Arg<OutboxMessageType>();
                _dbContext.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TrackingCode = packageEvent.TrackingCode,
                    Type = type,
                    Payload = System.Text.Json.JsonSerializer.Serialize(packageEvent),
                    OccurredAt = DateTimeOffset.UtcNow
                });
            });
        
        var mockLogger = Substitute.For<ILogger<PackageService>>();
        
        _packageService = new PackageService(_dbContext, _mockRepository, _mockQueue, mockLogger);
    }
    
    private static CreatePackageRequest CreateValidPackageRequest() =>
        new() {
            ParcelShopId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            OriginAddressId = Guid.NewGuid(),
            DestinationAddressId = Guid.NewGuid(),
            WeightKg = 2.0m
        };

    private static UpdatePackageRequest CreateValidUpdatePackageRequest(string trackingCode) =>
        new (
            TrackingCode: trackingCode,
            Status: PackageStatus.InTransit,
            CurrentHubId: Guid.NewGuid(),
            Message: "Package is in transit"
        );
        
    private static Package CreatePackage(string trackingCode) =>
        new (
            TrackingCode: trackingCode,
            ParcelShopId: Guid.NewGuid(),
            SenderId: Guid.NewGuid(),
            OriginAddressId: Guid.NewGuid(),
            DestinationAddressId: Guid.NewGuid(),
            WeightKg: 2.0m);

    [Fact]
    public async Task CreatePackageAsync_ShouldCreatePackageAndEnqueueEvent_WhenRequestIsValid()
    {
        // Arrange
        var request = CreateValidPackageRequest();

        _mockRepository.CreateAsync(Arg.Any<Package>())
            .Returns(args => Task.FromResult(args.Arg<Package>()));

        // Act
        var result = await _packageService.CreatePackageAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.TrackingCode);
        Assert.StartsWith("CTT-9Z-", result.TrackingCode);
        
        await _mockRepository.Received(1).CreateAsync(Arg.Any<Package>());
        
        _mockQueue.Received(1).Enqueue(
            Arg.Is<PackageEvent>(e => e.TrackingCode == result.TrackingCode),
            OutboxMessageType.Create);

        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(1, outboxMessagesInDb);
    }

    [Fact]
    public async Task CreatePackageAsync_ShouldCommitTransaction_WhenOperationSucceeds()
    {
        // Arrange
        var request = CreateValidPackageRequest();

        _mockRepository.CreateAsync(Arg.Any<Package>())
            .Returns(args => Task.FromResult(args.Arg<Package>()));

        // Act
        var result = await _packageService.CreatePackageAsync(request);

        // Assert
        Assert.NotNull(result);
        _mockQueue.Received(1).Enqueue(Arg.Any<PackageEvent>(), OutboxMessageType.Create);
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(1, outboxMessagesInDb);
    }

    [Fact]
    public async Task CreatePackageAsync_ShouldRollbackTransaction_WhenExceptionOccurs()
    {
        // Arrange
        var request = CreateValidPackageRequest();

        _mockRepository.CreateAsync(Arg.Any<Package>())
            .Returns<Task<Package>>(_ => throw new InvalidOperationException("Database error"));

        // Act
        var result = await _packageService.CreatePackageAsync(request);

        // Assert
        Assert.Null(result);
        _mockQueue.DidNotReceive().Enqueue(Arg.Any<PackageEvent>(), Arg.Any<OutboxMessageType>());
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(0, outboxMessagesInDb);
    }

    [Fact]
    public async Task UpdatePackageStatusAsync_ShouldUpdatePackageAndEnqueueEvent_WhenPackageExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var existingPackage = CreatePackage(trackingCode) with
        {
            Id = Guid.NewGuid(),
            CurrentStatus = PackageStatus.Created
        };

        var updateRequest = CreateValidUpdatePackageRequest(trackingCode);

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns(existingPackage);

        _mockRepository.UpdateAsync(Arg.Any<Package>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _packageService.UpdatePackageStatusAsync(updateRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(trackingCode, result.TrackingCode);
        Assert.Equal(PackageStatus.InTransit, result.RequestedStatus);
        Assert.True(result.Enqueued);

        await _mockRepository.Received(1).GetByTrackingCodeAsync(trackingCode);
        await _mockRepository.Received(1).UpdateAsync(Arg.Is<Package>(p => 
            p.CurrentStatus == PackageStatus.InTransit && 
            p.CurrentHubId == updateRequest.CurrentHubId));
        
        _mockQueue.Received(1).Enqueue(
            Arg.Is<PackageEvent>(e => 
                e.TrackingCode == trackingCode &&
                e.Status == PackageStatus.InTransit),
            OutboxMessageType.Update);
        
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(1, outboxMessagesInDb);
    }
    
    [Fact]
    public async Task UpdatePackageStatusAsync_ShouldCommitTransaction_WhenOperationSucceeds()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var existingPackage = CreatePackage(trackingCode) with
        {
            Id = Guid.NewGuid(),
            CurrentStatus = PackageStatus.Created
        };

        var updateRequest = CreateValidUpdatePackageRequest(trackingCode);

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns(existingPackage);

        _mockRepository.UpdateAsync(Arg.Any<Package>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _packageService.UpdatePackageStatusAsync(updateRequest);

        // Assert
        Assert.NotNull(result);
        _mockQueue.Received(1).Enqueue(Arg.Any<PackageEvent>(), OutboxMessageType.Update);
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(1, outboxMessagesInDb);
    }

    [Fact]
    public async Task UpdatePackageStatusAsync_ShouldReturnNull_WhenPackageNotFound()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-99999999";
        var updateRequest = CreateValidUpdatePackageRequest(trackingCode);

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns((Package?)null);

        // Act
        var result = await _packageService.UpdatePackageStatusAsync(updateRequest);

        // Assert
        Assert.Null(result);
        await _mockRepository.Received(1).GetByTrackingCodeAsync(trackingCode);
        await _mockRepository.DidNotReceive().UpdateAsync(Arg.Any<Package>());
        
        // Verify Enqueue was never called when package not found
        _mockQueue.DidNotReceive().Enqueue(Arg.Any<PackageEvent>(), Arg.Any<OutboxMessageType>());
        
        // Verify no outbox message was persisted
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(0, outboxMessagesInDb);
    }

    [Fact]
    public async Task UpdatePackageStatusAsync_ShouldRollbackTransaction_WhenExceptionOccurs()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var existingPackage = CreatePackage(trackingCode) with
        {
            Id = Guid.NewGuid(),
            CurrentStatus = PackageStatus.Created
        };

        var updateRequest = CreateValidUpdatePackageRequest(trackingCode);

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns(existingPackage);

        _mockRepository.UpdateAsync(Arg.Any<Package>())
            .Returns<Task>(_ => throw new InvalidOperationException("Update failed"));

        // Act
        var result = await _packageService.UpdatePackageStatusAsync(updateRequest);

        // Assert
        Assert.Null(result);
        _mockQueue.DidNotReceive().Enqueue(Arg.Any<PackageEvent>(), Arg.Any<OutboxMessageType>());
        var outboxMessagesInDb = await _dbContext.OutboxMessages.CountAsync();
        Assert.Equal(0, outboxMessagesInDb);
    }

    [Fact]
    public async Task GetPackageByTrackingCodeAsync_ShouldReturnPackage_WhenExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-12345678";
        var expectedPackage = CreatePackage(trackingCode);

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns(expectedPackage);

        // Act
        var result = await _packageService.GetPackageByTrackingCodeAsync(trackingCode);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(trackingCode, result.TrackingCode);
        await _mockRepository.Received(1).GetByTrackingCodeAsync(trackingCode);
    }

    [Fact]
    public async Task GetPackageByTrackingCodeAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        const string trackingCode = "CTT-9Z-99999999";

        _mockRepository.GetByTrackingCodeAsync(trackingCode)
            .Returns((Package?)null);

        // Act
        var result = await _packageService.GetPackageByTrackingCodeAsync(trackingCode);

        // Assert
        Assert.Null(result);
        await _mockRepository.Received(1).GetByTrackingCodeAsync(trackingCode);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
    }
}

