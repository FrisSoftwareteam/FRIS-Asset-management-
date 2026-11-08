using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers.Api;

[ApiController]
[Route("api/audit")]
public class AuditApiController(AuditScanService auditScanService, RoleGuard roleGuard) : ControllerBase
{
    [HttpPost("periods/{id:int}/scans/{scanNumber:int}")]
    public async Task<IActionResult> UploadScanBatch(int id, int scanNumber, [FromBody] AuditScanBatchVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var username = roleGuard.GetCurrentUserName(HttpContext);
        var (success, message, scan) = await auditScanService.AddScanBatchAsync(
            id, scanNumber, vm.RfidCodes, "external_reader", username);

        if (!success)
        {
            return BadRequest(new { Message = message });
        }

        return Ok(new
        {
            Message = message,
            ScanId = scan!.Id,
            scan.ScanNumber,
            scan.ItemCount,
            scan.DuplicateInBatchCount,
            scan.ScannedAt
        });
    }

    [HttpGet("periods/{id:int}/scans")]
    public async Task<IActionResult> GetScans(int id)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Auditor, RoleName.Backoffice))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        var period = await auditScanService.GetPeriodAsync(id);
        if (period is null)
        {
            return NotFound();
        }

        var scans = await auditScanService.GetTemporaryScanListAsync(id);
        return Ok(scans.Select(s => new
        {
            s.Id,
            s.ScanNumber,
            s.Source,
            s.ItemCount,
            s.DuplicateInBatchCount,
            s.ScannedAt
        }));
    }
}
