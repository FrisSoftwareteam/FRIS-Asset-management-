using FrislEams.Web.Data;
using FrislEams.Web.Domain;
using FrislEams.Web.Models;
using FrislEams.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FrislEams.Web.Controllers.Api;

[ApiController]
[Route("api/assets")]
public class AssetsApiController(
    AppDbContext db,
    AssetLifecycleService lifecycleService,
    TagCodeGenerator tagCodeGenerator,
    RoleGuard roleGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAssets()
    {
        var assets = await db.Assets
            .Include(a => a.AssetCategory)
            .Include(a => a.CurrentLocation)
            .OrderByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.TagCode,
                a.AssetName,
                Category = a.AssetCategory!.Name,
                Status = a.CurrentStatus.ToString(),
                Location = a.CurrentLocation!.Name,
                a.CurrentCondition
            })
            .ToListAsync();

        return Ok(assets);
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsset([FromBody] AssetRegistrationVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice, RoleName.Auditor))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (await db.RfidTags.AsQueryable().AnyAsync(r => r.RfidCode == vm.RfidCode.Trim()))
        {
            return Conflict("RFID code already exists.");
        }

        var asset = new Asset
        {
            TagCode = tagCodeGenerator.Generate(vm.AssetCategoryId, vm.InitialDepartmentId),
            AssetName = vm.AssetName,
            Description = vm.Description,
            AssetCategoryId = vm.AssetCategoryId,
            PurchaseDate = vm.PurchaseDate,
            PurchaseCost = vm.PurchaseCost,
            GlCode = vm.GlCode,
            StateOfPurchase = vm.StateOfPurchase,
            SupplierId = vm.SupplierId,
            SerialNumber = vm.SerialNumber,
            TagNumber = vm.TagNumber,
            WarrantyExpiryDate = vm.WarrantyExpiryDate,
            ExpectedServiceYears = vm.ExpectedServiceYears,
            CurrentCondition = vm.CurrentCondition,
            CurrentLocationId = vm.InitialLocationId,
            CurrentDepartmentId = vm.InitialDepartmentId,
            CurrentStatus = AssetStatus.UnregisteredUnassigned
        };

        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        db.RfidTags.Add(new RfidTag { AssetId = asset.Id, RfidCode = vm.RfidCode.Trim() });
        lifecycleService.ChangeStatus(asset, AssetStatus.RegisteredUnassigned, "Asset registration completed", "API");
        await db.SaveChangesAsync();

        return Created($"/api/assets/{asset.Id}", new { asset.Id, asset.TagCode });
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] StatusChangeVm vm)
    {
        if (!roleGuard.HasAnyRole(this, RoleName.Admin, RoleName.Backoffice))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Your role is not allowed to do this." });
        }

        var asset = await db.Assets.FindAsync(id);
        if (asset is null)
        {
            return NotFound();
        }

        var ok = lifecycleService.ChangeStatus(asset, vm.NextStatus, vm.Reason, vm.ChangedBy);
        if (!ok)
        {
            return BadRequest("Invalid transition or missing reason.");
        }

        await db.SaveChangesAsync();
        return Ok(new { asset.Id, Status = asset.CurrentStatus.ToString() });
    }
}
