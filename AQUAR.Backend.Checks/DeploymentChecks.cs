using AQUAR.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

internal static class DeploymentChecks
{
    public static async Task Run()
    {
        void Check(bool success,string name){if(!success)throw new Exception(name);Console.WriteLine("PASS DEPLOY: "+name);}
        IConfiguration Config(Dictionary<string,string?> values)=>new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        void Reject(Action action,string name){try{action();throw new Exception("Unexpected success: "+name);}catch(InvalidOperationException){Check(true,name);}}
        Check(DeploymentSettings.ListenUrl(Config(new(){{"PORT","12345"}}))=="http://0.0.0.0:12345","Render PORT binds all interfaces");
        Check(DeploymentSettings.ListenUrl(Config(new()))==null,"Without PORT preserve normal ASP.NET binding");
        foreach(var value in new[]{"0","65536","bad"})Reject(()=>DeploymentSettings.ListenUrl(Config(new(){{"PORT",value}})),"Invalid PORT rejected: "+value);
        Check(DeploymentSettings.AllowedOrigins(Config(new())).Length==0,"CORS denies browser origins unless configured");
        foreach(var origin in new[]{"*","https://example.invalid/path","https://example.invalid/"})
            Reject(()=>DeploymentSettings.AllowedOrigins(Config(new(){{"Cors:AllowedOrigins:0",origin}})),"Unsafe CORS origin rejected");
        var secret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var values=new Dictionary<string,string?>{
            ["ConnectionStrings:Default"]="Server=sql.example.invalid,1433;Database=AQUAR_DB;Encrypt=True;TrustServerCertificate=False;Integrated Security=False",
            ["Jwt:Key"]=secret
        };
        DeploymentSettings.ValidateProduction(Config(values));Check(true,"Production configuration accepted without connecting SQL");
        var invalid=new Dictionary<string,string?>(values){["ConnectionStrings:Default"]="Server=localhost;Database=AQUAR_DB;Integrated Security=True"};
        Reject(()=>DeploymentSettings.ValidateProduction(Config(invalid)),"Windows Authentication rejected in production deployment");
        invalid=new(values){["ConnectionStrings:Default"]="Server=sql.example.invalid;Database=AQUAR_DB;Encrypt=True;TrustServerCertificate=True"};
        Reject(()=>DeploymentSettings.ValidateProduction(Config(invalid)),"Untrusted SQL certificate bypass rejected");
        invalid=new(values){["Jwt:Key"]=""};
        Reject(()=>DeploymentSettings.ValidateProduction(Config(invalid)),"Missing production JWT secret rejected");

        // Process-local environment, restored below. No real database connection or migration.
        var variables=new Dictionary<string,string?>{
            ["ConnectionStrings__Default"]=values["ConnectionStrings:Default"],
            ["Jwt__Key"]=secret,["Jwt__Issuer"]="deployment-checks",["Jwt__Audience"]="deployment-checks",
            ["Cors__AllowedOrigins__0"]="https://frontend.example.invalid",["Swagger__Enabled"]="false",["PORT"]=null
        };
        var original=variables.Keys.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
        try
        {
            foreach(var item in variables)Environment.SetEnvironmentVariable(item.Key,item.Value);
            using var factory=new WebApplicationFactory<ApiEntryPoint>().WithWebHostBuilder(b=>b.UseEnvironment("Production"));
            using var client=factory.CreateClient();
            using var health=await client.GetAsync("/health");
            Check(health.StatusCode==HttpStatusCode.OK,"Anonymous health works with unreachable SQL");
            var payload=await health.Content.ReadFromJsonAsync<JsonElement>();
            Check(payload.EnumerateObject().Count()==1&&payload.GetProperty("status").GetString()=="ok","Health exposes no configuration or database details");
            using var swagger=await client.GetAsync("/swagger/v1/swagger.json");
            Check(swagger.StatusCode==HttpStatusCode.NotFound,"Swagger disabled in production by default");
            async Task<HttpResponseMessage> Preflight(string origin){
                using var request=new HttpRequestMessage(HttpMethod.Options,"/api/auth/login");
                request.Headers.Add("Origin",origin);request.Headers.Add("Access-Control-Request-Method","POST");
                request.Headers.Add("Access-Control-Request-Headers","content-type,authorization");
                return await client.SendAsync(request);
            }
            using var allowed=await Preflight("https://frontend.example.invalid");
            Check(allowed.Headers.TryGetValues("Access-Control-Allow-Origin",out var headers)&&headers.Single()=="https://frontend.example.invalid","Allowed frontend receives CORS headers");
            using var denied=await Preflight("https://untrusted.example.invalid");
            Check(!denied.Headers.Contains("Access-Control-Allow-Origin"),"Unknown frontend receives no CORS permission");
            using var unauthorized=await client.GetAsync("/api/me");
            Check(unauthorized.StatusCode==HttpStatusCode.Unauthorized,"Health/CORS do not bypass business authentication");
        }
        finally{foreach(var item in original)Environment.SetEnvironmentVariable(item.Key,item.Value);}
    }
}
