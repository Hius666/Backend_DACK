using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
namespace AQUAR.Api;
[ApiController,Authorize,Route("api")]
public class ReportsController(AppDb db,AccessService access,ReportService reports,EmailService email):ControllerBase {
 [HttpGet("reports")]public async Task<IActionResult> Report(int staffId,DateTime from,DateTime to){
 await access.Require(User,staffId);return Ok(await reports.Calculate(staffId,from,to));
 }
 [HttpGet("statistics")]public async Task<IActionResult> Statistics(string period,DateOnly date,int? staffId){
 var id=staffId??access.Id(User);await access.Require(User,id);
 var range=ReportService.Period(period,date);return Ok(await reports.Calculate(id,range.From,range.To));
 }
 [HttpGet("payroll")]public async Task<IActionResult> Payroll(string period,DateOnly date,int? staffId){
 if(period!="week"&&period!="month")return BadRequest(new{message="Bảng lương hỗ trợ week hoặc month."});
 return await Statistics(period,date,staffId);
 }
 [HttpPost("reports/email")]public async Task<IActionResult> Email(int staffId,DateTime from,DateTime to){
 await access.Require(User,staffId);var r=await reports.Calculate(staffId,from,to);
 var s=(await db.Staff.FindAsync(staffId))!;
 await email.Send(s.Email,s.Name,r,HttpContext.RequestAborted);return Ok(new{sentTo=s.Email});
 }
}

