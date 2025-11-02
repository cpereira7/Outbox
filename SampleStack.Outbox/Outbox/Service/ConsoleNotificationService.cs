using Outbox.Model;

namespace Outbox.Service;

public class ConsoleNotificationService : INotificationService
{
    private readonly ILogger<ConsoleNotificationService> _logger;

    public ConsoleNotificationService(ILogger<ConsoleNotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendPackageUpdateNotificationAsync(string trackingCode, PackageStatus status, string? message = null)
    {
        // Simulate notification delay (network call, email sending, etc.)
        Thread.Sleep(Random.Shared.Next(100, 500));

        var notificationMessage = $"[NOTIFICATION] Package '{trackingCode}' status changed to '{status}'";
        if (!string.IsNullOrEmpty(message))
        {
            notificationMessage += $" - {message}";
        }

        _logger.LogInformation(notificationMessage);

        return Task.CompletedTask;
    }
}

