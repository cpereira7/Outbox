using Outbox.Api.DTOs;
using Outbox.Model;

namespace Outbox.Service;

public interface IPackageService
{
    Task<CreatePackageResponse?> CreatePackageAsync(CreatePackageRequest request);
    Task<UpdatePackageResponse?> UpdatePackageStatusAsync(UpdatePackageRequest request);
    Task<Package?> GetPackageByTrackingCodeAsync(string trackingCode);
}

