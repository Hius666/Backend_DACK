using AQUAR.Api;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

try
{
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine($"PASS: {name}"); }
var week = ReportService.Period("week", new DateOnly(2026, 11, 5));
Check(week.From == new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc) && week.To-week.From == TimeSpan.FromDays(7), "Vietnam week starts Monday");
var month = ReportService.Period("month", new DateOnly(2024, 2, 15));
Check(month.To-month.From == TimeSpan.FromDays(29), "Leap February");
var quarter = ReportService.Period("quarter", new DateOnly(2026, 12, 31));
Check(quarter.To == new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc), "Quarter crosses year boundary");
var year = ReportService.Period("year", new DateOnly(2024, 6, 1));
Check(year.To-year.From == TimeSpan.FromDays(366), "Leap year");
var begin=DateTime.UtcNow.Date;
Check(ReportService.Hours(new[]{
 new Attendance{CheckInUtc=begin.AddHours(-2),CheckOutUtc=begin.AddHours(3)},
 new Attendance{CheckInUtc=begin.AddHours(22),CheckOutUtc=begin.AddHours(26)},
 new Attendance{CheckInUtc=begin.AddHours(4)},
 new Attendance{CheckInUtc=begin.AddDays(-2),CheckOutUtc=begin.AddDays(-1)}
},begin,begin.AddDays(1))==5m,"Clip shifts to report boundaries; exclude open and outside shifts");
try { ReportService.Period("day", new DateOnly(2026, 1, 1)); throw new Exception("Invalid period accepted"); }
catch (ApiException e) { Check(e.Status == 400, "Invalid period rejected"); }
var invalid = new StaffRequest("", "bad-email", "short", (UserRole)99, -1, null);
var errors = new List<ValidationResult>();
foreach(var parameter in typeof(StaffRequest).GetConstructors().Single().GetParameters()) {
 var value=typeof(StaffRequest).GetProperty(parameter.Name!)!.GetValue(invalid);
 Validator.TryValidateValue(value,new ValidationContext(invalid){MemberName=parameter.Name},errors,parameter.GetCustomAttributes(typeof(ValidationAttribute),false).Cast<ValidationAttribute>());
}
Check(errors.Count >= 5, "Invalid staff DTO rejected (record constructor metadata)");
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["Jwt:Key"]="test-only-key-at-least-thirty-two-bytes-long", ["Jwt:Issuer"]="checks", ["Jwt:Audience"]="flutter"
}).Build();
foreach(var role in Enum.GetValues<UserRole>()) {
 var raw = new TokenService(cfg).Create(new Staff{Id=42, Name="Test", Role=role});
 var token = new JwtSecurityTokenHandler().ReadJwtToken(raw);
 Check(token.Claims.Any(c=>c.Type==ClaimTypes.Role && c.Value==role.ToString()) && token.Audiences.Contains("flutter") && token.ValidTo > DateTime.UtcNow.AddHours(7), $"JWT {role} claims and expiry");
}

Console.WriteLine("All 10 backend checks passed.");
await PasswordWorkflowChecks.Run();
await DeploymentChecks.Run();
await AttendanceMethodChecks.Run();
LocationChecks.Run();
await LocationMigrationChecks.Run();
await StaffReportChecks.Run();
if(args.Contains("--integration"))await AuthIntegrationChecks.Run();
return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 1;
}

