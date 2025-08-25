using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Outbox.Api.DTOs;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Service;
using Outbox.Model;
using Outbox.Service;

namespace Outbox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PackageController : ControllerBase
{
    private readonly PackageDbContext _dbContext;
    private readonly PackageManager _packageManager;

    public PackageController(PackageDbContext dbContext, PackageManager packageManager)
    {
        _dbContext = dbContext;
        _packageManager = packageManager;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePackageResponse), 201)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request)
    {
        var result = await _packageManager.CreatePackageAsync(request);

        return result == null
            ? Problem("Failed to create package.", statusCode: 500)
            : CreatedAtAction(nameof(GetPackage), new { trackingCode = result.TrackingCode }, result);
    }

    [HttpPost("update")]
    [ProducesResponseType(typeof(UpdatePackageResponse), 202)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> UpdatePackage([FromBody] UpdatePackageRequest request)
    {
        var response = await _packageManager.UpdatePackageAsync(request);

        if (response == null)
            return NotFound("Package not found.");

        return !response.Enqueued 
            ? Problem("Failed to enqueue update event.", statusCode: 500) 
            : Accepted(response);
    }

    [HttpGet("{trackingCode}")]
    [ProducesResponseType(typeof(Package), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetPackage(string trackingCode)
    {
        var package = await _packageManager.GetPackageByTrackingCodeAsync(trackingCode);
        
        return package == null
            ? Problem("Failed to get package.", statusCode: 500)
            : Ok(package);
    }
}