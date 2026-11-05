using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
[ApiController,Route("api")]
public class AuthController(AppDb db,TokenService tokens,AccessService access,PasswordService passwords,PasswordWorkflow workflow):ControllerBase {
 [AllowAnonymous,HttpPost("auth/login"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
 public async Task<IActionResult> Login(LoginRequest r) {
 var email=r.Email.Trim();
 var s=await db.Staff.FirstOrDefaultAsync(x=>x.Email==email&&x.Active);
 if(!passwords.Verify(r.Password,s?.PasswordHash))return Unauthorized(new{message="Email hoặc mật khẩu không đúng."});
 if(s==null)return Unauthorized();
 return Ok(new{token=tokens.Create(s),mustChangePassword=s.MustChangePassword,user=new{s.Id,s.Name,s.Email,role=s.Role.ToString(),s.MustChangePassword}});
 }
 [Authorize,HttpGet("me")]
 public async Task<IActionResult> Me()=>Ok(AccessService.View((await db.Staff.FindAsync(access.Id(User)))!));
 [Authorize,HttpPut("account/password"),AllowWhilePasswordChangeRequired]
 public async Task<IActionResult> Password(PasswordRequest r) {
 var s=(await db.Staff.FindAsync(access.Id(User)))!;
 workflow.Change(s,r);await db.SaveChangesAsync();return Ok(new{message="Đã đổi mật khẩu. Vui lòng đăng nhập lại."});
 }
 [Authorize(Roles="Admin"),HttpPut("accounts/{id:int}/password")]
 public async Task<IActionResult> Reset(int id,ResetPasswordRequest r){
 var s=await db.Staff.FindAsync(id);if(s==null)return NotFound();
 if(id==access.Id(User))return BadRequest(new{message="Hãy dùng API đổi mật khẩu cho tài khoản của bạn."});
 workflow.Reset(s,r.NewPassword);await db.SaveChangesAsync();return Ok(new{message="Đã đặt mật khẩu tạm thời; nhân viên phải đổi mật khẩu khi đăng nhập."});
 }
 [Authorize,HttpPost("auth/logout"),AllowWhilePasswordChangeRequired]
 public async Task<IActionResult> Logout(){
 var s=(await db.Staff.FindAsync(access.Id(User)))!;
 PasswordWorkflow.RevokeAllTokens(s);await db.SaveChangesAsync();return NoContent();
 }
 [Authorize(Roles="Admin"),HttpGet("accounts")]
 public async Task<IActionResult> Accounts()=>Ok((await db.Staff.AsNoTracking().ToListAsync()).Select(AccessService.View));
}


