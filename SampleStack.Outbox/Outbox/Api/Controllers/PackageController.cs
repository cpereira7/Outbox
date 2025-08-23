using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Outbox.Api.DTOs;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Service;
using Outbox.Model;

namespace Outbox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackageController : ControllerBase
{
    private readonly PackageDbContext _dbContext;
    private readonly PackageEventService _packageEventService;

    public PackageController(PackageDbContext dbContext, PackageEventService packageEventService)
    {
        _dbContext = dbContext;
        _packageEventService = packageEventService;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var package = new Package(
                GenerateTrackingCode(),
                request.ParcelShopId,
                request.SenderId,
                request.OriginAddressId,
                request.DestinationAddressId,
                request.WeightKg
            );

            _dbContext.Packages.Add(package);
            await _dbContext.SaveChangesAsync();
        
            var eventAdded = await _packageEventService.CreatePackageEventAsync(
                package, 
                "New Package created.", 
                PackageStatus.Created);
            
            if (!eventAdded)
            {
                await transaction.RollbackAsync();
                return Problem("Failed to create package event.", statusCode: 500);
            }
            
            await transaction.CommitAsync();

            var response = new CreatePackageResponse(package.TrackingCode, package.CreatedAt);

            return CreatedAtAction(nameof(GetPackage), new { trackingCode = package.TrackingCode }, response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{trackingCode}")]
    public async Task<IActionResult> UpdatePackage(string trackingCode, [FromBody] UpdatePackageRequest request)
    {
        try
        {
            var package = await _dbContext.Packages.FirstOrDefaultAsync(p => p.TrackingCode == trackingCode);

            if (package == null)
            {
                return NotFound("Package not found in the system.");
            }
            
            var eventAdded = await _packageEventService.CreatePackageEventAsync(trackingCode, request.Message, request.Status, request.CurrentHubId);

            if (!eventAdded)
            {
                return Problem("Failed to add package event.", statusCode: 500);
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: 500);
        }
    }

    [HttpGet("{trackingCode}")]
    public IActionResult GetPackage(string trackingCode)
    {
        var package = _dbContext.Packages.FirstOrDefault(p => p.TrackingCode == trackingCode);

        if (package == null)
        {
            return NotFound();
        }
        return Ok(package);
    }

    private string GenerateTrackingCode() => $"CTT-9Z-{Random.Shared.Next(1000000000, 1999999999)}";
}