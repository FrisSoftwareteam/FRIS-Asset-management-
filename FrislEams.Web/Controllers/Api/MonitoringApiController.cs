using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers.Api;

[ApiController]
[Route("api/monitoring")]
public class MonitoringApiController(
    AppDbContext db,
    RfidMonitoringService rfidMonitoringService,
    ReportingService reportingService,
    RoleGuard roleGuard) : ControllerBase
{
    [HttpPost("rfid-scan")]
    public async Task<IActionResult> ProcessDoorScan([FromBody] RfidScanVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await rfidMonitoringService.ProcessDoorScanAsync(vm.RfidCode.Trim(), vm.DoorLocation.Trim());
        return Ok(result);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetRecentEvents()
    {
        var eventsLog = await db.RfidEvents
            .OrderByDescending(e => e.EventTime)
            .Take(200)
            .Select(e => new
            {
                e.Id,
                e.RfidCode,
                e.AssetId,
                e.EventType,
                e.DoorLocation,
                e.ProcessedStatus,
                e.AlertTriggered,
                e.EventTime
            })
            .ToListAsync();

        return Ok(eventsLog);
    }

    [HttpGet("assets-report")]
    public async Task<IActionResult> GetAssetsReport(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? departmentId,
        [FromQuery] int? categoryId,
        [FromQuery] string? status)
    {
        var filter = new ReportFilterVm
        {
            FromDate = fromDate,
            ToDate = toDate,
            DepartmentId = departmentId,
            CategoryId = categoryId,
            Status = status
        };

        var report = await reportingService.GetAssetsReportAsync(filter);
        return Ok(report.Select(a => new
        {
            a.Id,
            a.TagCode,
            a.AssetName,
            Status = a.CurrentStatus.ToString(),
            a.PurchaseDate,
            a.PurchaseCost
        }));
    }
}
