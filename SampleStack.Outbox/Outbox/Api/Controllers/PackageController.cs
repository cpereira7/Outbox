using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Outbox.Api.DTOs;
using Outbox.Infrastructure.Persistence;
using Outbox.Infrastructure.Service;
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
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request)
    {
        var result = await _packageManager.CreatePackageAsync(request);

        return result == null
            ? Problem("Failed to create package.", statusCode: 500)
            : CreatedAtAction(nameof(GetPackage), new { trackingCode = result.TrackingCode }, result);
    }

    [HttpPost("update")]
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
    public IActionResult GetPackage(string trackingCode)
    {
        var package = _dbContext.Packages.FirstOrDefault(p => p.TrackingCode == trackingCode);

        if (package == null)
        {
            return NotFound();
        }
        return Ok(package);
    }
}