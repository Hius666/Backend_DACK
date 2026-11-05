using AQUAR.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed class CapturingReportTransport : IReportEmailTransport
{
    public int Calls { get; private set; }
    public string? Recipient { get; private set; }
    public string? Body { get; private set; }
    public Exception? Failure { get; set; }
    public Task Send(MimeMessage message,SmtpSettings settings,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();Calls++;
        Recipient=message.To.Mailboxes.Single().Address;Body=message.TextBody;
        if(Failure!=null)throw Failure;
        return Task.CompletedTask;
    }
}

internal static class StaffReportChecks
{
    public static async Task Run()
    {
        void Check(bool success,string name){if(!success)throw new Exception(name);Console.WriteLine("PASS STAFF/REPORT: "+name);}
        IConfiguration Config(Dictionary<string,string?> values)=>new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var smtp=new Dictionary<string,string?>{["Smtp:Host"]="smtp.example.invalid",["Smtp:From"]="reports@example.invalid",["Smtp:Port"]="587"};
        var transport=new CapturingReportTransport();
        var report=new WorkReport(1,DateTime.UtcNow.Date.AddDays(-1),DateTime.UtcNow.Date,1,8,240000,"VND","test note");
        async Task Reject(Dictionary<string,string?> settings,int status,string name){
            try{await new EmailService(Config(settings),transport,NullLogger<EmailService>.Instance).Send("employee@example.invalid","Test",report);throw new Exception("Unexpected success: "+name);}
            catch(ApiException e){Check(e.Status==status,name);}
        }
        await Reject(new(),503,"Missing SMTP returns actionable 503 without network");
        await Reject(new(smtp){["Smtp:Port"]="bad"},503,"Invalid SMTP port rejected before sending");
        await Reject(new(smtp){["Smtp:From"]="invalid"},503,"Invalid sender rejected before sending");
        await Reject(new(smtp){["Smtp:User"]="fixture-user"},503,"Incomplete credentials rejected before sending");
        await Reject(new(smtp){["Smtp:Security"]="None"},503,"Plaintext SMTP rejected");
        Check(transport.Calls==0,"Invalid configuration never calls SMTP transport");
        Check(SmtpSettings.Read(Config(new(smtp){["Smtp:Port"]="465"})).Security==MailKit.Security.SecureSocketOptions.SslOnConnect,"Port 465 selects mandatory TLS");
        var mail=new EmailService(Config(smtp),transport,NullLogger<EmailService>.Instance);
        await mail.Send("employee@example.invalid","Test",report);
        Check(transport.Calls==1&&transport.Recipient=="employee@example.invalid"&&transport.Body!.Contains("UTC+7")&&transport.Body.Contains("test note"),"Email content and recipient use report data");
        transport.Failure=new IOException("test failure, do not expose details");
        try{await mail.Send("employee@example.invalid","Test",report);throw new Exception("Failure not reported");}
        catch(ApiException e){Check(e.Status==502&&!e.Message.Contains("do not expose"),"SMTP failure is sanitized 502");}
        transport.Failure=new OperationCanceledException();
        try{await mail.Send("employee@example.invalid","Test",report);throw new Exception("Timeout not reported");}
        catch(ApiException e){Check(e.Status==504,"SMTP timeout returns 504 without retry");}
        transport.Failure=null;
        try{ReportService.Period("year",DateOnly.MinValue);throw new Exception("Invalid date accepted");}
        catch(ApiException e){Check(e.Status==400,"Invalid calendar date returns 400 instead of server error");}

        // All fixtures below exist only in RAM. No SQL Server connection, seed or migration is used.
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        AppDb Context()=>new(new DbContextOptionsBuilder<AppDb>().UseSqlite(connection).Options);
        await using var fixture=Context();await fixture.Database.EnsureCreatedAsync();
        var department=new Department{Name="Fixture Department"};fixture.Departments.Add(department);await fixture.SaveChangesAsync();
        Staff Person(string name,UserRole role)=>new(){Name=name,Email=name+"@example.invalid",Role=role,PasswordHash=Convert.ToHexString(RandomNumberGenerator.GetBytes(32))};
        var admin=Person("admin",UserRole.Admin);var manager=Person("manager",UserRole.Manager);
        fixture.Staff.AddRange(admin,manager);await fixture.SaveChangesAsync();
        var employee=Person("employee",UserRole.Employee);employee.ManagerId=manager.Id;employee.DepartmentId=department.Id;employee.HourlyRate=30000;
        var outsider=Person("outsider",UserRole.Employee);fixture.Staff.AddRange(employee,outsider);await fixture.SaveChangesAsync();
        var from=DateTime.UtcNow.Date.AddDays(-1);var to=DateTime.UtcNow.Date;
        fixture.Attendances.Add(new Attendance{StaffId=employee.Id,CheckInUtc=from.AddHours(1),CheckOutUtc=from.AddHours(9),Method="manual"});await fixture.SaveChangesAsync();
        var originalHash=employee.PasswordHash;var originalVersion=employee.TokenVersion;
        var overrides=new Dictionary<string,string?>{["Jwt__Key"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),["Jwt__Issuer"]="management-checks",["Jwt__Audience"]="management-checks",["PORT"]=null};
        var original=overrides.Keys.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
        try
        {
            foreach(var item in overrides)Environment.SetEnvironmentVariable(item.Key,item.Value);
            var delivery=new CapturingReportTransport();
            using var factory=new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(builder=>{
                builder.UseEnvironment("Development");builder.ConfigureLogging(l=>l.SetMinimumLevel(LogLevel.Warning));
                builder.ConfigureTestServices(services=>{
                    services.RemoveAll<AppDb>();services.AddScoped(_=>Context());
                    services.RemoveAll<EmailService>();services.AddScoped(_=>new EmailService(Config(smtp),delivery,NullLogger<EmailService>.Instance));
                });
            });
            using var client=factory.CreateClient();
            var tokens=new TokenService(factory.Services.GetRequiredService<IConfiguration>());
            void As(Staff staff)=>client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",tokens.Create(staff));
            async Task Expect(HttpResponseMessage response,HttpStatusCode status,string name){using(response){Check(response.StatusCode==status,$"{name}: {(int)response.StatusCode}");await response.Content.ReadAsByteArrayAsync();}}
            await Expect(await client.GetAsync("/api/staff"),HttpStatusCode.Unauthorized,"Anonymous staff request denied");
            As(employee);await Expect(await client.GetAsync("/api/staff"),HttpStatusCode.Forbidden,"Employee cannot manage staff");
            As(manager);
            using(var response=await client.GetAsync("/api/staff")){
                Check(response.StatusCode==HttpStatusCode.OK,"Manager staff list succeeds");
                var rows=await response.Content.ReadFromJsonAsync<JsonElement>();
                Check(rows.GetArrayLength()==1&&rows[0].GetProperty("id").GetInt32()==employee.Id,"Manager sees only assigned employee");
            }
            await Expect(await client.PatchAsJsonAsync($"/api/staff/{employee.Id}/status",new{active=false}),HttpStatusCode.Forbidden,"Manager cannot activate/deactivate staff");
            As(admin);
            using(var response=await client.GetAsync("/api/staff")){
                var rows=await response.Content.ReadFromJsonAsync<JsonElement>();
                Check(response.StatusCode==HttpStatusCode.OK&&rows.GetArrayLength()==4,"Admin staff list succeeds");
                Check(rows.EnumerateArray().All(row=>!row.TryGetProperty("passwordHash",out _)),"Staff response never exposes password hash");
            }
            await Expect(await client.PatchAsJsonAsync($"/api/staff/{employee.Id}/status",new{}),HttpStatusCode.BadRequest,"Missing activation flag rejected");
            await Expect(await client.PatchAsJsonAsync($"/api/staff/{employee.Id}/status",new{active=false}),HttpStatusCode.OK,"Admin deactivates employee");
            await using(var read=Context()){
                var saved=(await read.Staff.AsNoTracking().SingleAsync(s=>s.Id==employee.Id));
                Check(!saved.Active&&saved.DepartmentId==department.Id&&saved.ManagerId==manager.Id&&saved.Role==UserRole.Employee&&saved.HourlyRate==30000&&saved.PasswordHash==originalHash&&saved.TokenVersion==originalVersion,"Status PATCH preserves department, assignment, role, pay and credentials");
            }
            await Expect(await client.PatchAsJsonAsync($"/api/staff/{employee.Id}/status",new{active=true}),HttpStatusCode.OK,"Admin reactivates employee");
            await Expect(await client.PatchAsJsonAsync($"/api/staff/{admin.Id}/status",new{active=false}),HttpStatusCode.BadRequest,"Last active Admin protection preserved");
            await Expect(await client.PostAsJsonAsync("/api/staff",new{name="Invalid",email="new@example.invalid",password=Convert.ToHexString(RandomNumberGenerator.GetBytes(5)),role=2,hourlyRate=30000}),HttpStatusCode.BadRequest,"Ten-character temporary password is rejected, reproducing old form mismatch");
            var temporaryPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            using(var response=await client.PostAsJsonAsync("/api/staff",new{name="Created fixture",email="created@example.invalid",password=temporaryPassword,role=2,hourlyRate=30000,managerId=manager.Id,departmentId=department.Id})){
                Check(response.StatusCode==HttpStatusCode.Created,"Admin creates staff with valid temporary password and assignment");
                var row=await response.Content.ReadFromJsonAsync<JsonElement>();
                Check(row.GetProperty("mustChangePassword").GetBoolean()&&row.GetProperty("managerId").GetInt32()==manager.Id,"Created account retains forced password change and Manager assignment");
            }
            string ReportUrl(int id)=>$"/api/reports?staffId={id}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
            string EmailUrl(int id)=>$"/api/reports/email?staffId={id}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
            As(employee);
            using(var response=await client.GetAsync(ReportUrl(employee.Id))){
                Check(response.StatusCode==HttpStatusCode.OK,"Employee reads own report");var row=await response.Content.ReadFromJsonAsync<JsonElement>();
                Check(row.GetProperty("hours").GetDecimal()==8&&row.GetProperty("grossSalary").GetDecimal()==240000,"Report and salary use attendance hours");
            }
            await Expect(await client.GetAsync(ReportUrl(outsider.Id)),HttpStatusCode.Forbidden,"Employee cannot read another report");
            await Expect(await client.PostAsync(EmailUrl(employee.Id),null),HttpStatusCode.OK,"Employee emails own report");
            Check(delivery.Recipient==employee.Email,"Email uses employee address from database, not client-supplied recipient");
            var sent=delivery.Calls;
            await Expect(await client.PostAsync(EmailUrl(outsider.Id),null),HttpStatusCode.Forbidden,"Employee cannot email another employee report");
            Check(delivery.Calls==sent,"Denied email never reaches transport");
            As(manager);await Expect(await client.PostAsync(EmailUrl(employee.Id),null),HttpStatusCode.OK,"Manager emails assigned employee report");
            await Expect(await client.PostAsync(EmailUrl(outsider.Id),null),HttpStatusCode.Forbidden,"Manager cannot email unassigned employee report");
            As(admin);await Expect(await client.GetAsync(ReportUrl(outsider.Id)),HttpStatusCode.OK,"Admin reads any employee report");
            delivery.Failure=new IOException("fixture failure");
            await Expect(await client.PostAsync(EmailUrl(employee.Id),null),HttpStatusCode.BadGateway,"HTTP email failure is not reported as success");
            foreach(var period in new[]{"week","month","quarter","year"})await Expect(await client.GetAsync($"/api/statistics?staffId={employee.Id}&period={period}&date=2026-11-05"),HttpStatusCode.OK,"Statistics period "+period);
            await Expect(await client.GetAsync($"/api/statistics?staffId={employee.Id}&period=year&date=0001-01-01"),HttpStatusCode.BadRequest,"Invalid statistics date is actionable 400");
        }
        finally{foreach(var item in original)Environment.SetEnvironmentVariable(item.Key,item.Value);}
    }
}
