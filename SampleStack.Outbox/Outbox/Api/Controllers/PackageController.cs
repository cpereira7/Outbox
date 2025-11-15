using Microsoft.AspNetCore.Mvc;
using Outbox.Api.DTOs;
using Outbox.Api.Filters;
using Outbox.Model;
using Outbox.Service;

namespace Outbox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ValidateModel]
public class PackageController : ControllerBase
{
    private readonly IPackageService _packageService;

    public PackageController(IPackageService packageService)
    {
        _packageService = packageService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePackageResponse), 201)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request)
    {
        var result = await _packageService.CreatePackageAsync(request);

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
        var response = await _packageService.UpdatePackageStatusAsync(request);

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
        var package = await _packageService.GetPackageByTrackingCodeAsync(trackingCode);
        
        return package == null
            ? NotFound("Package not found.")
            : Ok(package);
    }
}