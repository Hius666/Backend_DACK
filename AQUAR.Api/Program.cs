using AQUAR.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
var initializeAdmin=args.Contains("--init-admin");
var builder=WebApplication.CreateBuilder(args.Where(a=>a!="--init-admin").ToArray());
var listenUrl=DeploymentSettings.ListenUrl(builder.Configuration);
if(listenUrl!=null)builder.WebHost.UseUrls(listenUrl);
if(builder.Environment.IsProduction())DeploymentSettings.ValidateProduction(builder.Configuration);
var allowedOrigins=DeploymentSettings.AllowedOrigins(builder.Configuration);
builder.Services.AddCors(o=>o.AddPolicy("frontend",p=>p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<AppDb>(o=>o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddOptions<CompanyLocationOptions>().Bind(builder.Configuration.GetSection("CompanyLocation")).Validate(o=>o.IsValid(),"CompanyLocation needs valid coordinate pairs and a positive finite radius.").ValidateOnStart();
builder.Services.AddScoped<AttendanceLocationService>();
var key=builder.Configuration["Jwt:Key"]??throw new InvalidOperationException("JWT key missing");
if(Encoding.UTF8.GetByteCount(key)<32) throw new InvalidOperationException("JWT key must be >=32 bytes");
if(string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"])||string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]))throw new InvalidOperationException("JWT issuer and audience are required");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
 o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer=builder.Configuration["Jwt:Issuer"],ValidateAudience=true,ValidAudience=builder.Configuration["Jwt:Audience"],ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),ValidateLifetime=true,ClockSkew=TimeSpan.FromSeconds(30)};
 o.Events=new JwtBearerEvents{OnTokenValidated=async c=>{
 var id=c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
 var db=c.HttpContext.RequestServices.GetRequiredService<AppDb>();
 var staff=int.TryParse(id,out var n)?await db.Staff.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==n):null;
 if(staff==null||!staff.Active||staff.Role.ToString()!=c.Principal?.FindFirstValue(ClaimTypes.Role)||c.Principal?.FindFirstValue("credential_stamp")!=PasswordService.Stamp(staff.PasswordHash)||c.Principal?.FindFirstValue("token_version")!=staff.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)) c.Fail("Account or credentials changed");
 else c.HttpContext.Items[PasswordChangeGate.StateKey]=staff.MustChangePassword;
 }};
});
builder.Services.AddAuthorization();
builder.Services.AddControllers(o=>o.Filters.Add<ApiExceptionFilter>());
builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen(o=>{
 o.AddSecurityDefinition("Bearer",new OpenApiSecurityScheme{Type=SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT",Description="Dán JWT từ /api/auth/login (không thêm tiền tố Bearer)."});
 o.AddSecurityRequirement(new OpenApiSecurityRequirement{{new OpenApiSecurityScheme{Reference=new OpenApiReference{Type=ReferenceType.SecurityScheme,Id="Bearer"}},Array.Empty<string>()}});
});
builder.Services.AddRateLimiter(o=>{
 o.RejectionStatusCode=429;
 o.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
});
builder.Services.AddSingleton<PasswordService>();builder.Services.AddScoped<PasswordWorkflow>();builder.Services.AddScoped<AdminBootstrapService>();
builder.Services.AddScoped<AccessService>();builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ReportService>();builder.Services.AddScoped<EmailService>();builder.Services.AddScoped<IReportEmailTransport,ReportEmailTransport>();
var app=builder.Build();
if(initializeAdmin)return await AdminBootstrapService.RunConsole(app.Services);
app.UseExceptionHandler(h=>h.Run(async c=>{
 var e=c.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
 c.Response.StatusCode=e is ApiException a?a.Status:500;
 await c.Response.WriteAsJsonAsync(new{message=e is ApiException?e.Message:"Lỗi máy chủ. Vui lòng thử lại."});
}));
if(app.Environment.IsDevelopment()||app.Configuration.GetValue<bool>("Swagger:Enabled")){app.UseSwagger();app.UseSwaggerUI();}
app.UseRouting();app.UseCors("frontend");app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();app.UseMiddleware<PasswordChangeGate>();
app.MapGet("/health",()=>Results.Ok(new{status="ok"})).AllowAnonymous();
app.MapControllers();await app.RunAsync();
return 0;
public partial class Program { }

