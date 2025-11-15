using System.ComponentModel.DataAnnotations;
using Outbox.Model;

namespace Outbox.Api.DTOs;

public record UpdatePackageRequest
{
    [StringLength(50, MinimumLength = 1, ErrorMessage = "TrackingCode must be valid.")]
    public required string TrackingCode { get; init; }
    
    public required PackageStatus Status { get; init; }
    
    public required Guid CurrentHubId { get; init; }
    
    [StringLength(500, ErrorMessage = "Message must not exceed 500 characters")]
    public string? Message { get; init; }
}
