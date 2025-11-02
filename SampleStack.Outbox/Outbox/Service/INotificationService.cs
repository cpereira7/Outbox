using Outbox.Model;

namespace Outbox.Service;

public interface INotificationService
{
    Task SendPackageUpdateNotificationAsync(string trackingCode, PackageStatus status, string? message = null);
}

