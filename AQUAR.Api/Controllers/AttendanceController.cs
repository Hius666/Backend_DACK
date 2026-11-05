using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
[ApiController,Authorize,Route("api/attendance")]
public class AttendanceController(AppDb db,AccessService access,AttendanceLocationService locations):ControllerBase {
 [HttpPost("check-in")]public async Task<IActionResult> In(CheckRequest r){
 if(r.Method is not ("manual" or "biometric"))return BadRequest(new{message="Phương thức chấm công phải là manual hoặc biometric."});
 var id=access.Id(User);
 await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
 if(await db.Attendances.AnyAsync(a=>a.StaffId==id&&a.CheckOutUtc==null))return Conflict(new{message="Ca làm chưa kết thúc."});
 // Manual=true is reserved for manager-entered corrections, not self-service check-in.
 // Neither method label is proof of biometric verification.
 var a=new Attendance{StaffId=id,CheckInUtc=DateTime.UtcNow,Method=r.Method,Manual=false};
 a.Locations.Add(locations.Capture(AttendanceEvent.CheckIn,r.Latitude,r.Longitude,a.CheckInUtc));
 db.Attendances.Add(a);await db.SaveChangesAsync();await tx.CommitAsync();
 return Ok(View(a));
 }
 [HttpPost("check-out")] public async Task<IActionResult> Out(CheckRequest r){
 if(r.Method is not ("manual" or "biometric"))return BadRequest(new{message="Phương thức chấm công phải là manual hoặc biometric."});
 await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
 var a=await db.Attendances.Include(x=>x.Locations).FirstOrDefaultAsync(x=>x.StaffId==access.Id(User)&&x.CheckOutUtc==null);
 if(a==null)return BadRequest(new{message="Chưa check-in."});
 var serverTime=DateTime.UtcNow;
 a.Locations.Add(locations.Capture(AttendanceEvent.CheckOut,r.Latitude,r.Longitude,serverTime));
 a.CheckOutUtc=serverTime;await db.SaveChangesAsync();await tx.CommitAsync();return Ok(View(a));
 }
 [Authorize(Roles="Admin,Manager"),HttpPost("manual")]
 public async Task<IActionResult> Manual(ManualRequest r){
 await access.Require(User,r.StaffId);
 if(!await db.Staff.AnyAsync(x=>x.Id==r.StaffId&&x.Active))return NotFound();
 if(r.CheckInUtc.Kind==DateTimeKind.Unspecified||r.CheckOutUtc.Kind==DateTimeKind.Unspecified)return BadRequest(new{message="Thời gian phải có múi giờ."});
 var start=r.CheckInUtc.ToUniversalTime();var end=r.CheckOutUtc.ToUniversalTime();
 if(end<=start||end-start>TimeSpan.FromHours(24)||end>DateTime.UtcNow||string.IsNullOrWhiteSpace(r.Reason))return BadRequest(new{message="Thời gian hoặc lý do không hợp lệ."});
 await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
 if(await db.Attendances.AnyAsync(a=>a.StaffId==r.StaffId&&a.CheckInUtc<end&&(a.CheckOutUtc==null||a.CheckOutUtc>start)))return Conflict(new{message="Trùng ca làm."});
 var a=new Attendance{StaffId=r.StaffId,CheckInUtc=start,CheckOutUtc=end,Method="manual",Manual=true,RecordedBy=access.Id(User),Reason=r.Reason.Trim()};
 db.Attendances.Add(a);await db.SaveChangesAsync();await tx.CommitAsync();return Ok(View(a));
 }
 [HttpGet("history")]public async Task<IActionResult> History(int? staffId){
 var id=staffId??access.Id(User);await access.Require(User,id);
 var rows=await db.Attendances.AsNoTracking().Include(x=>x.Locations).Where(a=>a.StaffId==id).OrderByDescending(a=>a.CheckInUtc).Take(200).ToListAsync();
 return Ok(rows.Select(View));
 }
 static object View(Attendance a)=>new{a.Id,a.StaffId,a.CheckInUtc,a.CheckOutUtc,a.Method,a.Manual,a.RecordedBy,a.Reason,
 checkInLocation=AttendanceLocationService.View(a.Locations.FirstOrDefault(l=>l.Event==AttendanceEvent.CheckIn)),
 checkOutLocation=a.CheckOutUtc==null?null:AttendanceLocationService.View(a.Locations.FirstOrDefault(l=>l.Event==AttendanceEvent.CheckOut))};
}

