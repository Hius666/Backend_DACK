using Microsoft.EntityFrameworkCore;
namespace AQUAR.Api;
public record WorkReport(int StaffId,DateTime From,DateTime To,int Shifts,decimal Hours,decimal GrossSalary,string Currency,string Note);
public class ReportService(AppDb db) {
 public async Task<WorkReport> Calculate(int id,DateTime from,DateTime to){
 if(from.Kind==DateTimeKind.Unspecified||to.Kind==DateTimeKind.Unspecified)throw new ApiException(400,"Thời gian phải có múi giờ.");
 from=from.ToUniversalTime();to=to.ToUniversalTime();
 if(to<=from||to-from>TimeSpan.FromDays(366))throw new ApiException(400,"Khoảng thời gian không hợp lệ (tối đa 366 ngày).");
 var staff=await db.Staff.AsNoTracking().FirstOrDefaultAsync(s=>s.Id==id)??throw new ApiException(404,"Không tìm thấy nhân viên.");
 var rows=await db.Attendances.AsNoTracking().Where(a=>a.StaffId==id&&a.CheckOutUtc!=null&&a.CheckInUtc<to&&a.CheckOutUtc>from).ToListAsync();
 var hours=Hours(rows,from,to);
 return new(id,from,to,rows.Count,Math.Round(hours,2),Math.Round(hours*staff.HourlyRate,2),"VND","Lương gộp dự kiến theo đơn giá giờ hiện tại, chưa gồm OT, nghỉ phép, thuế và bảo hiểm.");
 }
 public static decimal Hours(IEnumerable<Attendance> rows,DateTime from,DateTime to)=>rows.Where(a=>a.CheckOutUtc.HasValue&&a.CheckInUtc<to&&a.CheckOutUtc>from).Sum(a=>(decimal)((a.CheckOutUtc!.Value<to?a.CheckOutUtc.Value:to)-(a.CheckInUtc>from?a.CheckInUtc:from)).TotalHours);
 public static (DateTime From,DateTime To) Period(string period,DateOnly date){
 if(string.IsNullOrWhiteSpace(period)||date.Year is < 2 or > 9998)throw new ApiException(400,"Kỳ báo cáo hoặc ngày không hợp lệ.");
 // Calendar boundaries in Vietnam (UTC+7); weeks start on Monday.
 var d=date.ToDateTime(TimeOnly.MinValue);
 var start=period.ToLowerInvariant() switch{
 "week"=>d.AddDays(-((int)d.DayOfWeek+6)%7),
 "month"=>new DateTime(d.Year,d.Month,1),
 "quarter"=>new DateTime(d.Year,((d.Month-1)/3)*3+1,1),
 "year"=>new DateTime(d.Year,1,1),
 _=>throw new ApiException(400,"period phải là week, month, quarter hoặc year.")
 };
 var end=period.ToLowerInvariant() switch{"week"=>start.AddDays(7),"month"=>start.AddMonths(1),"quarter"=>start.AddMonths(3),_=>start.AddYears(1)};
 return(new DateTimeOffset(start,TimeSpan.FromHours(7)).UtcDateTime,new DateTimeOffset(end,TimeSpan.FromHours(7)).UtcDateTime);
 }
}

