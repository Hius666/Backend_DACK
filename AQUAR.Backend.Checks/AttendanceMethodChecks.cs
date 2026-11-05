using AQUAR.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

internal static class AttendanceMethodChecks
{
    public static async Task Run()
    {
        void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS ATTENDANCE: "+name);}
        await using var connection=new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        AppDb Context()=>new(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options);
        await using var setup=Context();
        // This creates ephemeral SQLite tables only, not SQL Server or EF migrations.
        await setup.Database.EnsureCreatedAsync();
        var employee=new Staff{Name="Attendance check",Email="attendance@example.invalid",Role=UserRole.Employee,PasswordHash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32))};
        var manager=new Staff{Name="Manager check",Email="manager@example.invalid",Role=UserRole.Manager,PasswordHash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32))};
        setup.Staff.AddRange(employee,manager);await setup.SaveChangesAsync();
        var overrides=new Dictionary<string,string?>{
            ["Jwt__Key"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt__Issuer"]="attendance-checks",["Jwt__Audience"]="attendance-checks",["PORT"]=null,
            ["CompanyLocation__Latitude"]="0",["CompanyLocation__Longitude"]="0",["CompanyLocation__RadiusMeters"]="100"
        };
        var original=overrides.Keys.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
        try
        {
            foreach(var pair in overrides)Environment.SetEnvironmentVariable(pair.Key,pair.Value);
            using var factory=new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(b=>{
                b.UseEnvironment("Development");
                b.ConfigureLogging(l=>l.SetMinimumLevel(LogLevel.Warning));
                b.ConfigureTestServices(s=>{s.RemoveAll<AppDb>();s.AddScoped(_=>Context());});
            });
            using var client=factory.CreateClient();
            var configuration=factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var tokens=new TokenService(configuration);
            async Task Expect(HttpResponseMessage response,HttpStatusCode status,string name){using(response){Check(response.StatusCode==status,$"{name}: {(int)response.StatusCode}");await response.Content.ReadAsByteArrayAsync();}}
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual"}),HttpStatusCode.Unauthorized,"Manual requires JWT");
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",tokens.Create(employee));
            using(var entered=await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual"})){
                Check(entered.StatusCode==HttpStatusCode.OK,"Manual self check-in accepted");
                var json=await entered.Content.ReadFromJsonAsync<JsonElement>();
                Check(json.GetProperty("method").GetString()=="manual"&&!json.GetProperty("manual").GetBoolean(),"Ordinary manual is not biometric or manager correction");
                Check(json.GetProperty("staffId").GetInt32()==employee.Id&&json.GetProperty("checkOutUtc").ValueKind==JsonValueKind.Null,"JWT owns open shift");
                Check(json.GetProperty("checkInLocation").GetProperty("status").GetString()=="Unverified","No GPS still accepted as Unverified");
            }
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual"}),HttpStatusCode.Conflict,"Duplicate open shift rejected");
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-out",new{method="nfc"}),HttpStatusCode.BadRequest,"Unknown checkout method rejected");
            using(var exited=await client.PostAsJsonAsync("/api/attendance/check-out",new{method="manual"})){
                Check(exited.StatusCode==HttpStatusCode.OK,"Manual check-out accepted");
                var json=await exited.Content.ReadFromJsonAsync<JsonElement>();
                Check(json.GetProperty("method").GetString()=="manual"&&json.GetProperty("checkOutUtc").ValueKind==JsonValueKind.String,"Manual method retained and checkout recorded");
            }
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-out",new{method="manual"}),HttpStatusCode.BadRequest,"Checkout without open shift rejected");
            foreach(var method in new[]{"nfc","MANUAL",""})await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method}),HttpStatusCode.BadRequest,"Unknown check-in method rejected");
            using(var history=await client.GetAsync("/api/attendance/history")){
                Check(history.StatusCode==HttpStatusCode.OK,"Personal history accepted");
                var json=await history.Content.ReadFromJsonAsync<JsonElement>();
                Check(json.GetArrayLength()==1&&json[0].GetProperty("method").GetString()=="manual","History persists manual without relabeling");
            }
            await Expect(await client.PostAsJsonAsync("/api/attendance/manual",new{staffId=employee.Id,checkInUtc=DateTime.UtcNow.AddDays(-2),checkOutUtc=DateTime.UtcNow.AddDays(-2).AddHours(1),reason="checks"}),HttpStatusCode.Forbidden,"Self manual does not grant manager correction permission");
            var beforeGps=await setup.Attendances.CountAsync();
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual",latitude=91,longitude=0}),HttpStatusCode.BadRequest,"Invalid GPS rejected before saving shift");
            Check(await setup.Attendances.CountAsync()==beforeGps,"Invalid GPS does not leave partial attendance");
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual",latitude=0}),HttpStatusCode.BadRequest,"Partial GPS rejected");
            using(var gpsIn=await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual",latitude=0,longitude=0.00045})){
                Check(gpsIn.StatusCode==HttpStatusCode.OK,"GPS check-in accepted");
                var data=await gpsIn.Content.ReadFromJsonAsync<JsonElement>();
                Check(data.GetProperty("checkInLocation").GetProperty("status").GetString()=="InRange","GPS check-in within 100m");
                Check(data.GetProperty("checkInLocation").GetProperty("recordedAtUtc").GetString()==data.GetProperty("checkInUtc").GetString(),"GPS timestamp equals server check-in time");
            }
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual",latitude=0,longitude=0.002}),HttpStatusCode.Conflict,"Duplicate GPS check-in rejected");
            using(var gpsOut=await client.PostAsJsonAsync("/api/attendance/check-out",new{method="manual",latitude=0,longitude=0.002})){
                Check(gpsOut.StatusCode==HttpStatusCode.OK,"Outside GPS checkout is not refused");
                var data=await gpsOut.Content.ReadFromJsonAsync<JsonElement>();
                Check(data.GetProperty("checkOutLocation").GetProperty("status").GetString()=="OutOfRange"&&data.GetProperty("checkInLocation").GetProperty("status").GetString()=="InRange","Check-in/out locations stored independently");
            }
            Check(await setup.AttendanceLocationAlerts.CountAsync()==1,"Exactly one outside-radius alert persisted");
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-out",new{method="manual",latitude=0,longitude=0.002}),HttpStatusCode.BadRequest,"Duplicate GPS checkout rejected");
            Check(await setup.AttendanceLocationAlerts.CountAsync()==1,"Duplicate checkout creates no duplicate alert");
            var legacy=new Attendance{StaffId=employee.Id,CheckInUtc=DateTime.UtcNow.AddYears(-1),CheckOutUtc=DateTime.UtcNow.AddYears(-1).AddHours(1),Method="biometric",Reason="legacy-kept"};
            setup.Attendances.Add(legacy);await setup.SaveChangesAsync();
            using(var oldHistory=await client.GetAsync("/api/attendance/history")){
                var data=await oldHistory.Content.ReadFromJsonAsync<JsonElement>();
                var old=data.EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==legacy.Id);
                Check(old.GetProperty("reason").GetString()=="legacy-kept"&&old.GetProperty("method").GetString()=="biometric"&&old.GetProperty("checkInLocation").GetProperty("status").GetString()=="Unverified","Legacy row retained without GPS backfill");
            }
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="biometric"}),HttpStatusCode.OK,"Legacy biometric label remains compatible");
            using(var mixed=await client.PostAsJsonAsync("/api/attendance/check-out",new{method="manual"})){
                Check(mixed.StatusCode==HttpStatusCode.OK,"Manual checkout can close an existing legacy shift");
                var json=await mixed.Content.ReadFromJsonAsync<JsonElement>();
                Check(json.GetProperty("method").GetString()=="biometric","Existing check-in method is not rewritten on checkout");
            }
            await Expect(await client.GetAsync($"/api/attendance/history?staffId={manager.Id}"),HttpStatusCode.Forbidden,"Employee cannot read another staff history");
            employee.MustChangePassword=true;await setup.SaveChangesAsync();
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual"}),HttpStatusCode.Forbidden,"Mandatory password change still blocks attendance");
            employee.MustChangePassword=false;employee.Active=false;await setup.SaveChangesAsync();
            await Expect(await client.PostAsJsonAsync("/api/attendance/check-in",new{method="manual"}),HttpStatusCode.Unauthorized,"Disabled account remains rejected");
        }
        finally{foreach(var pair in original)Environment.SetEnvironmentVariable(pair.Key,pair.Value);}
    }
}
