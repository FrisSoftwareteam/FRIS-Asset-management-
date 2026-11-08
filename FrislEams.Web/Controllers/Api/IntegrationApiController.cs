using System.Text.Json;
using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers.Api;

[ApiController]
[Route("api/integration")]
public class IntegrationApiController(IIntegrationQueue queue, AppDbContext db, RoleGuard roleGuard) : ControllerBase
{
    [HttpPost("procurement-sync")]
    public async Task<IActionResult> QueueProcurementSync([FromBody] ProcurementSyncVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Supplier))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var payload = JsonSerializer.Serialize(vm);
        await queue.QueueAsync(new IntegrationJob("ProcurementSync", vm.SourceSystem, payload));
        return Accepted(new { Message = "Procurement sync queued", Count = vm.Items.Count });
    }

    [HttpPost("rfid-batch")]
    public async Task<IActionResult> QueueRfidBatch([FromBody] RfidBatchScanVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var payload = JsonSerializer.Serialize(vm);
        await queue.QueueAsync(new IntegrationJob("RfidBatchScan", vm.SourceSystem, payload));
        return Accepted(new { Message = "RFID batch queued", Count = vm.Scans.Count });
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetIntegrationEvents()
    {
        var eventsLog = await db.IntegrationEventLogs
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .ToListAsync();

        return Ok(eventsLog);
    }
}
