using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Outbox.Infrastructure.PackageQueue;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Processor;
using Outbox.Model;
using Outbox.Service;
using Testcontainers.PostgreSql;

namespace Outbox.Tests.Infrastructure.Processor;

/// <summary>
/// Full end-to-end integration tests for the PackageEventQueueProcessor background service.
/// Tests the complete workflow including the background processor running in parallel.
/// </summary>
public class PackageEventQueueProcessorEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private PackageDbContext _dbContext = null!;
    private IHost _host = null!;

    public PackageEventQueueProcessorEndToEndTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithDockerEndpoint("tcp://localhost:2375")
            .WithImage("postgres:16-alpine")
            .WithDatabase("outbox_e2e_test")
            .WithUsername("test_user")
            .WithPassword("test_pass")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        // Clear static notification list
        TestNotificationService.NotificationsSent.Clear();

        // Build a complete host with the background service
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<PackageDbContext>(options =>
                    options.UseNpgsql(_postgresContainer.GetConnectionString()));

                services.AddScoped<INotificationService, TestNotificationService>();
                services.AddHostedService<PackageEventQueueProcessor>();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            })
            .Build();

        // Get DbContext and apply migrations
        using var scope = _host.Services.CreateScope();
        _dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
        await _dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        await _postgresContainer.DisposeAsync();
    }

    [Fact]
    public async Task BackgroundProcessor_ShouldProcessOutboxMessages_Automatically()
    {
        // Arrange: Create pending outbox messages
        var packageEvent1 = new PackageEvent(
            TrackingCode: "CTT-9Z-E2E-001",
            Status: PackageStatus.InTransit,
            Location: Guid.NewGuid(),
            Message: "Package 1 in transit"
        );

        var packageEvent2 = new PackageEvent(
            TrackingCode: "CTT-9Z-E2E-002",
            Status: PackageStatus.Delivered,
            Location: Guid.NewGuid(),
            Message: "Package 2 delivered"
        );

        var message1 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent1.TrackingCode,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent1),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            IsCanceled = false
        };

        var message2 = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = packageEvent2.TrackingCode,
            Type = OutboxMessageType.Update,
            Payload = JsonSerializer.Serialize(packageEvent2),
            OccurredAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            IsCanceled = false
        };

        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            dbContext.OutboxMessages.AddRange(message1, message2);
            await dbContext.SaveChangesAsync();
        }

        // Act: Start the background processor
        await _host.StartAsync();

        // Wait for the processor to pick up and process the messages
        // The processor runs every 30 seconds, so we give it some time
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert: Messages should be processed
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            
            var processedMessage1 = await dbContext.OutboxMessages.FindAsync(message1.Id);
            var processedMessage2 = await dbContext.OutboxMessages.FindAsync(message2.Id);

            Assert.NotNull(processedMessage1);
            Assert.NotNull(processedMessage2);
            
            // Both should be marked as completed
            Assert.True(processedMessage1.IsCompleted);
            Assert.True(processedMessage2.IsCompleted);
            Assert.NotNull(processedMessage1.ProcessedAt);
            Assert.NotNull(processedMessage2.ProcessedAt);
        }

        // Verify notifications were sent (tracked via static list)
        Assert.Equal(2, TestNotificationService.NotificationsSent.Count);
    }

    [Fact]
    public async Task BackgroundProcessor_ShouldHandleConcurrentUpdates_Gracefully()
    {
        // Arrange: Create a message
        var packageEvent = new PackageEvent(
            TrackingCode: "CTT-9Z-E2E-CONCURRENT",
            Status: PackageStatus.InTransit,
            Location: Guid.NewGuid(),
            Message: "Concurrent test"
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

        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            dbContext.OutboxMessages.Add(outboxMessage);
            await dbContext.SaveChangesAsync();
        }

        // Act: Start processor and simultaneously try to manually update
        await _host.StartAsync();

        // Simulate manual update (competing with background processor)
        var updateTask = Task.Run(async () =>
        {
            await Task.Delay(100); // Small delay
            using var scope = _host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            
            var message = await dbContext.OutboxMessages.FindAsync(outboxMessage.Id);
            if (message != null && !message.IsCompleted)
            {
                message.IsCanceled = true;
                message.ProcessedAt = DateTimeOffset.UtcNow;
                
                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Expected if processor beat us to it
                }
            }
        });

        await Task.WhenAll(updateTask, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert: Message should be either completed or canceled (one operation won)
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            var finalMessage = await dbContext.OutboxMessages.FindAsync(outboxMessage.Id);
            
            Assert.NotNull(finalMessage);
            Assert.True(finalMessage.IsCompleted || finalMessage.IsCanceled);
            Assert.NotNull(finalMessage.ProcessedAt);
        }
    }

    [Fact]
    public async Task BackgroundProcessor_ShouldHandleInvalidPayload_ByCancelingMessage()
    {
        // Arrange: Create a message with invalid JSON payload
        var invalidMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TrackingCode = "CTT-9Z-INVALID",
            Type = OutboxMessageType.Update,
            Payload = "{invalid json", // Malformed JSON
            OccurredAt = DateTimeOffset.UtcNow,
            IsCompleted = false,
            IsCanceled = false
        };

        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            dbContext.OutboxMessages.Add(invalidMessage);
            await dbContext.SaveChangesAsync();
        }

        // Act: Start the processor
        await _host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert: Message should be canceled due to JSON error
        using (var scope = _host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PackageDbContext>();
            var processedMessage = await dbContext.OutboxMessages.FindAsync(invalidMessage.Id);
            
            Assert.NotNull(processedMessage);
            Assert.True(processedMessage.IsCanceled);
            Assert.NotNull(processedMessage.ProcessedAt);
        }
    }

    /// <summary>
    /// Test notification service that tracks sent notifications
    /// </summary>
    public class TestNotificationService : INotificationService
    {
        public static readonly List<(string TrackingCode, PackageStatus Status, string? Message)> NotificationsSent = new();

        public Task SendPackageUpdateNotificationAsync(string trackingCode, PackageStatus status, string? message)
        {
            NotificationsSent.Add((trackingCode, status, message));
            return Task.CompletedTask;
        }
    }
}

