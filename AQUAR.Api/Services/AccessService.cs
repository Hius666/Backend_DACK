using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
public class ApiException(int status,string message):Exception(message){public int Status {get;}=status;}
public class AccessService(AppDb db) {
 public int Id(ClaimsPrincipal p)=>int.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
 public async Task Require(ClaimsPrincipal p,int id) {
 if(id==Id(p)||p.IsInRole("Admin"))return;
 if(p.IsInRole("Manager")&&await db.Staff.AnyAsync(s=>s.Id==id&&s.ManagerId==Id(p)))return;
 throw new ApiException(403,"Không có quyền truy cập nhân viên này.");
 }
 public async Task ValidateAssignment(int? manager,int? department,int? self=null) {
 if(manager.HasValue&&(manager==self||!await db.Staff.AnyAsync(s=>s.Id==manager&&s.Active&&s.Role==UserRole.Manager)))throw new ApiException(400,"Quản lý không hợp lệ.");
 if(department.HasValue&&!await db.Departments.AnyAsync(d=>d.Id==department))throw new ApiException(400,"Phòng ban không tồn tại.");
 }
 public static object View(Staff s)=>new{s.Id,s.Name,s.Email,role=s.Role,s.Active,s.HourlyRate,s.ManagerId,s.DepartmentId,s.MustChangePassword};
}


