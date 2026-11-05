using AQUAR.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

internal static class AuthIntegrationChecks
{
    public static async Task Run()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "AQUAR.Api")).AddJsonFile("appsettings.json").Build();
        var connectionString = config.GetConnectionString("Default")!;
        var target = new SqlConnectionStringBuilder(connectionString);
        if (target.InitialCatalog != "AQUAR_DB" || !target.IntegratedSecurity)
            throw new Exception("Integration checks require AQUAR_DB with Windows Authentication.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        AppDb NewContext()
        {
            var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlServer(connection).Options);
            db.Database.UseTransaction(transaction);
            return db;
        }
        await using var fixtures = NewContext();
        var baseline = await fixtures.Staff.CountAsync();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var suffix = Guid.NewGuid().ToString("N");
        var passwords = new PasswordService();
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS SQL/API: " + name); }
        try
        {
            var bootstrap = new AdminBootstrapService(fixtures, passwords);
            var hasAdmin = await fixtures.Staff.AnyAsync(s => s.Role == UserRole.Admin);
            Staff admin;
            if (!hasAdmin)
            {
                admin = await bootstrap.Create($"admin-{suffix}@example.invalid", "Integration Admin", password);
                Check(admin.Role == UserRole.Admin && passwords.Verify(password, admin.PasswordHash), "First Admin bootstrap hashes password");
            }
            else
            {
                // Never touch an existing Admin; the fixture is rolled back below.
                admin = new Staff { Name="Integration Admin", Email=$"admin-{suffix}@example.invalid", Role=UserRole.Admin, PasswordHash=passwords.Hash(password) };
                fixtures.Staff.Add(admin); await fixtures.SaveChangesAsync();
            }
            try { await bootstrap.Create($"blocked-{suffix}@example.invalid", "Blocked", password); throw new Exception("Bootstrap allowed second Admin"); }
            catch (ApiException e) { Check(e.Status == 409, "Bootstrap refuses when Admin exists"); }
            var manager = new Staff { Name="Integration Manager", Email=$"manager-{suffix}@example.invalid", Role=UserRole.Manager, PasswordHash=passwords.Hash(password) };
            fixtures.Staff.Add(manager); await fixtures.SaveChangesAsync();
            var employee = new Staff { Name="Integration Employee", Email=$"employee-{suffix}@example.invalid", Role=UserRole.Employee, ManagerId=manager.Id, PasswordHash=passwords.Hash(password) };
            var outsider = new Staff { Name="Integration Outsider", Email=$"outsider-{suffix}@example.invalid", Role=UserRole.Employee, PasswordHash=passwords.Hash(password) };
            fixtures.Staff.AddRange(employee, outsider); await fixtures.SaveChangesAsync();
            using var factory = new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(Path.Combine(root,"AQUAR.Api"));
                builder.ConfigureAppConfiguration((_, cfg) => cfg.AddConfiguration(config));
                builder.ConfigureLogging(logging => logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<AppDb>();
                    services.AddScoped(_ => NewContext());
                });
            });
            using var client = factory.CreateClient();
            async Task Expect(HttpResponseMessage response, HttpStatusCode expected, string name)
            {
                using(response) { Check(response.StatusCode==expected, $"{name}: {(int)response.StatusCode}"); await response.Content.ReadAsByteArrayAsync(); }
            }
            async Task<string> Login(Staff staff)
            {
                client.DefaultRequestHeaders.Authorization=null;
                using var response=await client.PostAsJsonAsync("/api/auth/login",new{email=staff.Email,password});
                Check(response.StatusCode==HttpStatusCode.OK,$"{staff.Role} login");
                var json=await response.Content.ReadFromJsonAsync<JsonElement>();
                Check(json.GetProperty("user").GetProperty("role").GetString()==staff.Role.ToString(),$"{staff.Role} response matches Flutter contract");
                return json.GetProperty("token").GetString()!;
            }
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Anonymous rejected");
            await Expect(await client.PostAsJsonAsync("/api/auth/login",new{email=admin.Email,password="wrong-password"}),HttpStatusCode.Unauthorized,"Wrong password rejected");
            await Expect(await client.PostAsJsonAsync("/api/auth/login",new{email=$"missing-{suffix}@example.invalid",password}),HttpStatusCode.Unauthorized,"Unknown account rejected");
            var adminToken=await Login(admin);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
            await Expect(await client.GetAsync("/api/accounts"),HttpStatusCode.OK,"Admin lists accounts");
            await Expect(await client.PostAsJsonAsync("/api/staff",new{name="Invalid",email=$"invalid-{suffix}@example.invalid",password="short",role=2,hourlyRate=0}),HttpStatusCode.BadRequest,"Weak password rejected by API validation");
            await Expect(await client.PostAsJsonAsync("/api/staff",new{name="Invalid",email=$"invalid-{suffix}@example.invalid",password=new string('a',73),role=2,hourlyRate=0}),HttpStatusCode.BadRequest,"BCrypt byte limit enforced");
            var managerToken=await Login(manager);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",managerToken);
            await Expect(await client.GetAsync("/api/accounts"),HttpStatusCode.Forbidden,"Manager cannot list accounts");
            await Expect(await client.GetAsync($"/api/staff/{employee.Id}"),HttpStatusCode.OK,"Manager accesses assigned employee");
            await Expect(await client.GetAsync($"/api/staff/{outsider.Id}"),HttpStatusCode.Forbidden,"Manager cannot access outsider");
            var employeeToken=await Login(employee);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",employeeToken);
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.OK,"Employee accesses own profile");
            await Expect(await client.GetAsync("/api/staff"),HttpStatusCode.Forbidden,"Employee cannot list staff");
            await Expect(await client.GetAsync($"/api/attendance/history?staffId={outsider.Id}"),HttpStatusCode.Forbidden,"Employee cannot access another history");
            var newPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await Expect(await client.PutAsJsonAsync("/api/account/password",new{currentPassword=password,newPassword,confirmNewPassword="mismatch"}),HttpStatusCode.BadRequest,"Mismatched confirmation rejected");
            await Expect(await client.PutAsJsonAsync("/api/account/password",new{currentPassword="incorrect",newPassword,confirmNewPassword=newPassword}),HttpStatusCode.BadRequest,"Incorrect current password rejected");
            await Expect(await client.PutAsJsonAsync("/api/account/password",new{currentPassword=password,newPassword,confirmNewPassword=newPassword}),HttpStatusCode.OK,"Password change");
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Old JWT revoked after password change");
            await Expect(await client.PostAsJsonAsync("/api/auth/login",new{email=employee.Email,password}),HttpStatusCode.Unauthorized,"Old password rejected");
            await Expect(await client.PostAsJsonAsync("/api/auth/login",new{email=employee.Email,password=newPassword}),HttpStatusCode.OK,"New password accepted");
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",managerToken[..^2]+"xx");
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Tampered JWT rejected");
            var original=new JwtSecurityTokenHandler().ReadJwtToken(adminToken);
            string CustomToken(string issuer,string audience,DateTime expires,string signingKey)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer,audience,original.Claims.Where(c=>c.Type!="exp"&&c.Type!="nbf"&&c.Type!="iss"&&c.Type!="aud"),notBefore:expires.AddHours(-1),expires:expires,signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),SecurityAlgorithms.HmacSha256)));
            foreach(var scenario in new[]{
                ("Expired JWT",CustomToken(config["Jwt:Issuer"]!,config["Jwt:Audience"]!,DateTime.UtcNow.AddMinutes(-2),config["Jwt:Key"]!)),
                ("Wrong issuer",CustomToken("invalid-issuer",config["Jwt:Audience"]!,DateTime.UtcNow.AddMinutes(5),config["Jwt:Key"]!)),
                ("Wrong audience",CustomToken(config["Jwt:Issuer"]!,"invalid-audience",DateTime.UtcNow.AddMinutes(5),config["Jwt:Key"]!)),
                ("Wrong signing key",CustomToken(config["Jwt:Issuer"]!,config["Jwt:Audience"]!,DateTime.UtcNow.AddMinutes(5),Convert.ToHexString(RandomNumberGenerator.GetBytes(32))))
            }) {
                client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",scenario.Item2);
                await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,scenario.Item1);
            }
            manager.Role=UserRole.Employee;await fixtures.SaveChangesAsync();
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",managerToken);
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Role change revokes JWT");
            manager.Role=UserRole.Manager;
            manager.Active=false;await fixtures.SaveChangesAsync();
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",managerToken);
            await Expect(await client.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Disabled account JWT rejected");
            await Expect(await client.PostAsJsonAsync("/api/auth/login",new{email=manager.Email,password}),HttpStatusCode.Unauthorized,"Disabled account login rejected");
            // Verify the Swagger bearer security definition is actually emitted.
            using var swagger=await client.GetAsync("/swagger/v1/swagger.json");
            Check(swagger.IsSuccessStatusCode && (await swagger.Content.ReadAsStringAsync()).Contains("bearer"),"Swagger JWT authorization definition");
            client.DefaultRequestHeaders.Authorization=null;
            await Expect(await client.PostAsJsonAsync("/api/auth/register",new{}),HttpStatusCode.NotFound,"No public registration endpoint");
            // A separate host has its own login rate-limit window; real JWT validation and SQL stay enabled.
            using(var passwordFactory=factory.WithWebHostBuilder(_=>{}))
            using(var passwordClient=passwordFactory.CreateClient())
            {
                var temporary=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                var chosen=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                var tempEmail=$"temporary-{suffix}@example.invalid";
                passwordClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
                using var created=await passwordClient.PostAsJsonAsync("/api/staff",new{name="Temporary Employee",email=tempEmail,password=temporary,role=2,hourlyRate=30000,managerId=manager.Id});
                // manager was disabled above, so omit assignment for this fixture.
                Check(created.StatusCode==HttpStatusCode.BadRequest,"Inactive manager assignment rejected");
                using var createdWithoutManager=await passwordClient.PostAsJsonAsync("/api/staff",new{name="Temporary Employee",email=tempEmail,password=temporary,role=2,hourlyRate=30000});
                Check(createdWithoutManager.StatusCode==HttpStatusCode.Created,"Admin creates temporary-password employee");
                var newUser=await createdWithoutManager.Content.ReadFromJsonAsync<JsonElement>();
                var targetId=newUser.GetProperty("id").GetInt32();
                Check(newUser.GetProperty("mustChangePassword").GetBoolean(),"New employee requires password change");
                async Task<string> TemporaryLogin(string secret,bool mustChange)
                {
                    passwordClient.DefaultRequestHeaders.Authorization=null;
                    using var response=await passwordClient.PostAsJsonAsync("/api/auth/login",new{email=tempEmail,password=secret});
                    Check(response.StatusCode==HttpStatusCode.OK,"Temporary/password login accepted");
                    var data=await response.Content.ReadFromJsonAsync<JsonElement>();
                    Check(data.GetProperty("mustChangePassword").GetBoolean()==mustChange,"Login reports required-change state");
                    var token=data.GetProperty("token").GetString()!;
                    passwordClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
                    return token;
                }
                var temporaryToken=await TemporaryLogin(temporary,true);
                using(var denied=await passwordClient.GetAsync("/api/attendance/history"))
                {
                    Check(denied.StatusCode==HttpStatusCode.Forbidden,"Temporary account business API blocked");
                    Check((await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()=="MUST_CHANGE_PASSWORD","Required-change error code");
                }
                await Expect(await passwordClient.PutAsJsonAsync("/api/account/password",new{currentPassword=temporary,newPassword=chosen,confirmNewPassword="different"}),HttpStatusCode.BadRequest,"Forced change rejects confirmation mismatch");
                await Expect(await passwordClient.PutAsJsonAsync("/api/account/password",new{currentPassword=temporary,newPassword=chosen,confirmNewPassword=chosen}),HttpStatusCode.OK,"Forced first password change succeeds");
                await Expect(await passwordClient.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Temporary token revoked after change");
                var chosenToken=await TemporaryLogin(chosen,false);
                await Expect(await passwordClient.GetAsync("/api/attendance/history"),HttpStatusCode.OK,"Changed account can access business API");
                var reset=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                await Expect(await passwordClient.PutAsJsonAsync($"/api/accounts/{targetId}/password",new{newPassword=reset}),HttpStatusCode.Forbidden,"Employee cannot reset passwords");
                // Use a fresh Manager fixture because the original was disabled in prior checks.
                var resetManager=new Staff{Name="Reset Manager",Email=$"reset-manager-{suffix}@example.invalid",Role=UserRole.Manager,PasswordHash=passwords.Hash(password)};
                fixtures.Staff.Add(resetManager);await fixtures.SaveChangesAsync();
                passwordClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new TokenService(config).Create(resetManager));
                await Expect(await passwordClient.PutAsJsonAsync($"/api/accounts/{targetId}/password",new{newPassword=reset}),HttpStatusCode.Forbidden,"Manager cannot reset passwords");
                passwordClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",adminToken);
                await Expect(await passwordClient.PutAsJsonAsync($"/api/accounts/{targetId}/password",new{newPassword=reset}),HttpStatusCode.OK,"Admin resets password to temporary value");
                passwordClient.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",chosenToken);
                await Expect(await passwordClient.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Pre-reset JWT revoked");
                await TemporaryLogin(reset,true);
                await Expect(await passwordClient.GetAsync("/api/me"),HttpStatusCode.Forbidden,"Reset requires change before profile access");
                await Expect(await passwordClient.PostAsync("/api/auth/logout",null),HttpStatusCode.NoContent,"Forced account can logout");
                await Expect(await passwordClient.GetAsync("/api/me"),HttpStatusCode.Unauthorized,"Logout revokes JWT");
                await TemporaryLogin(reset,true);
                var finalPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                await Expect(await passwordClient.PutAsJsonAsync("/api/account/password",new{currentPassword=reset,newPassword=finalPassword,confirmNewPassword=finalPassword}),HttpStatusCode.OK,"Post-reset password change succeeds");
                await TemporaryLogin(finalPassword,false);
                await Expect(await passwordClient.GetAsync("/api/me"),HttpStatusCode.OK,"Post-reset account unlocked after change");
            }
            admin.Active=false;await fixtures.SaveChangesAsync();
            try { await bootstrap.Create($"blocked-again-{suffix}@example.invalid","Blocked",password);throw new Exception("Disabled Admin bypassed bootstrap guard"); }
            catch(ApiException e){Check(e.Status==409,"Disabled Admin does not reopen bootstrap");}
            var limited=false;
            for(var attempt=0;attempt<11;attempt++) {
                using var attemptResponse=await client.PostAsJsonAsync("/api/auth/login",new{email=$"missing-{suffix}@example.invalid",password});
                if(attemptResponse.StatusCode==HttpStatusCode.TooManyRequests){limited=true;break;}
                Check(attemptResponse.StatusCode==HttpStatusCode.Unauthorized,"Login failure before rate limit");
            }
            Check(limited,"Login rate limit returns 429");
        }
        finally { await transaction.RollbackAsync(); }
        await using var verify = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlServer(connectionString).Options);
        Check(await verify.Staff.CountAsync()==baseline && !await verify.Staff.AnyAsync(s=>s.Email.Contains(suffix)), "Transaction rolled back; no test accounts retained");
    }
}
