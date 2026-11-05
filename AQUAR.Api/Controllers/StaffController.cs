using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Data;
namespace AQUAR.Api;

[ApiController, Authorize(Roles = "Admin,Manager"), Route("api/staff")]
public class StaffController(AppDb db, AccessService access, PasswordService passwords) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var admin = User.IsInRole("Admin");
        var current = access.Id(User);
        var staff = await db.Staff.AsNoTracking().Where(s => admin || s.ManagerId == current).OrderBy(s => s.Name).ToListAsync();
        return Ok(staff.Select(AccessService.View));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        await access.Require(User, id);
        var staff = await db.Staff.FindAsync(id);
        return staff == null ? NotFound() : Ok(AccessService.View(staff));
    }

    [Authorize(Roles = "Admin"), HttpPost]
    public async Task<IActionResult> Create(StaffRequest request)
    {
        await access.ValidateAssignment(request.ManagerId, request.DepartmentId);
        var email = request.Email.Trim();
        if (await db.Staff.AnyAsync(s => s.Email == email)) return Conflict(new { message = "Email đã tồn tại." });
        var staff = new Staff { Name = request.Name.Trim(), Email = email, PasswordHash = passwords.Hash(request.Password),
            MustChangePassword = true, Role = request.Role, HourlyRate = request.HourlyRate,
            ManagerId = request.ManagerId, DepartmentId = request.DepartmentId };
        db.Staff.Add(staff);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = staff.Id }, AccessService.View(staff));
    }

    [Authorize(Roles = "Admin"), HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, StaffUpdate request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var staff = await db.Staff.FindAsync(id);
        if (staff == null) return NotFound();
        if (staff.Active && staff.Role == UserRole.Admin && (request.Role != UserRole.Admin || !request.Active) &&
            !await db.Staff.AnyAsync(s => s.Id != id && s.Active && s.Role == UserRole.Admin))
            return BadRequest(new { message = "Phải giữ ít nhất một Admin hoạt động." });
        if (staff.Role == UserRole.Manager && request.Role != UserRole.Manager && await db.Staff.AnyAsync(s => s.ManagerId == id))
            return BadRequest(new { message = "Cần chuyển nhân viên sang quản lý khác trước." });
        await access.ValidateAssignment(request.ManagerId, request.DepartmentId, id);
        var email = request.Email.Trim();
        if (await db.Staff.AnyAsync(s => s.Id != id && s.Email == email)) return Conflict(new { message = "Email đã tồn tại." });
        staff.Name = request.Name.Trim(); staff.Email = email; staff.Role = request.Role;
        staff.HourlyRate = request.HourlyRate; staff.ManagerId = request.ManagerId;
        staff.DepartmentId = request.DepartmentId; staff.Active = request.Active;
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Ok(AccessService.View(staff));
    }

    [Authorize(Roles = "Admin"), HttpPatch("{id:int}/status")]
    public async Task<IActionResult> Status(int id, StaffStatusRequest request)
    {
        if (!request.Active.HasValue) return BadRequest(new { message = "Cần chỉ định active." });
        return await SetActive(id, request.Active.Value);
    }

    [Authorize(Roles = "Admin"), HttpPatch("{id:int}/deactivate")]
    public Task<IActionResult> Deactivate(int id) => SetActive(id, false);

    private async Task<IActionResult> SetActive(int id, bool active)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var staff = await db.Staff.FindAsync(id);
        if (staff == null) return NotFound();
        if (!active && staff.Role == UserRole.Admin && !await db.Staff.AnyAsync(s => s.Id != id && s.Active && s.Role == UserRole.Admin))
            return BadRequest(new { message = "Phải giữ ít nhất một Admin hoạt động." });
        // Change only activation; never overwrite department/manager/role or credentials.
        staff.Active = active;
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Ok(AccessService.View(staff));
    }
}
