using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
[ApiController,Authorize,Route("api/departments")]
public class DepartmentsController(AppDb db):ControllerBase {
 [HttpGet] public async Task<IActionResult> List()=>Ok(await db.Departments.AsNoTracking().OrderBy(d=>d.Name).ToListAsync());
 [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id){var d=await db.Departments.FindAsync(id);return d==null?NotFound():Ok(d);}
 [Authorize(Roles="Admin"),HttpPost] public async Task<IActionResult> Create(DepartmentRequest r){
 var name=r.Name.Trim();if(await db.Departments.AnyAsync(d=>d.Name==name))return Conflict(new{message="Tên phòng ban đã tồn tại."});
 var d=new Department{Name=name,Description=r.Description};db.Departments.Add(d);await db.SaveChangesAsync();return CreatedAtAction(nameof(Get),new{id=d.Id},d);
 }
 [Authorize(Roles="Admin"),HttpPut("{id:int}")]public async Task<IActionResult> Update(int id,DepartmentRequest r){
 var d=await db.Departments.FindAsync(id);if(d==null)return NotFound();
 var name=r.Name.Trim();if(await db.Departments.AnyAsync(x=>x.Id!=id&&x.Name==name))return Conflict(new{message="Tên phòng ban đã tồn tại."});
 d.Name=name;d.Description=r.Description;await db.SaveChangesAsync();return Ok(d);
 }
}

