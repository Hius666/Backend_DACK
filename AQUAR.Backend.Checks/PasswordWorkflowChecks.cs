using AQUAR.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Security.Cryptography;

internal static class PasswordWorkflowChecks
{
    public static async Task Run()
    {
        var passwords=new PasswordService();
        var workflow=new PasswordWorkflow(passwords);
        string Secret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS PASSWORD: "+name);}
        void Reject(Action action,string name){try{action();throw new Exception("Unexpected success: "+name);}catch(ApiException e){Check(e.Status==400,name);}}
        foreach(var role in Enum.GetValues<UserRole>())
        {
            var temporary=Secret();var next=Secret();
            var staff=new Staff{Role=role,PasswordHash=passwords.Hash(temporary),MustChangePassword=true};
            var oldStamp=PasswordService.Stamp(staff.PasswordHash);
            Reject(()=>workflow.Change(staff,new PasswordRequest(temporary,next,Secret())),role+" mismatched confirmation rejected");
            Reject(()=>workflow.Change(staff,new PasswordRequest(Secret(),next,next)),role+" incorrect current password rejected");
            Reject(()=>workflow.Change(staff,new PasswordRequest(temporary,temporary,temporary)),role+" unchanged password rejected");
            Reject(()=>workflow.Change(staff,new PasswordRequest(temporary,"short","short")),role+" weak password rejected");
            Check(staff.MustChangePassword&&staff.TokenVersion==0&&PasswordService.Stamp(staff.PasswordHash)==oldStamp,role+" rejected requests preserve account state");
            workflow.Change(staff,new PasswordRequest(temporary,next,next));
            Check(!staff.MustChangePassword&&staff.TokenVersion==1&&passwords.Verify(next,staff.PasswordHash)&&!passwords.Verify(temporary,staff.PasswordHash),role+" first change clears flag and invalidates old credentials");
            Check(PasswordService.Stamp(staff.PasswordHash)!=oldStamp,role+" credential stamp rotates");
            var reset=Secret();workflow.Reset(staff,reset);
            Check(staff.MustChangePassword&&staff.TokenVersion==2&&passwords.Verify(reset,staff.PasswordHash)&&!passwords.Verify(next,staff.PasswordHash),role+" reset creates temporary password and requires change");
            PasswordWorkflow.RevokeAllTokens(staff);
            Check(staff.TokenVersion==3&&staff.MustChangePassword,role+" logout revokes sessions without bypassing required change");

            DefaultHttpContext Context(params object[] metadata){
                var context=new DefaultHttpContext();context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,role.ToString())},"checks"));
                context.Items[PasswordChangeGate.StateKey]=true;
                context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(metadata),"checks"));
                context.Response.Body=new MemoryStream();return context;
            }
            var blocked=Context();var called=false;
            await new PasswordChangeGate(_=>{called=true;return Task.CompletedTask;}).InvokeAsync(blocked);
            blocked.Response.Body.Position=0;var body=await new StreamReader(blocked.Response.Body).ReadToEndAsync();
            Check(!called&&blocked.Response.StatusCode==403&&body.Contains("MUST_CHANGE_PASSWORD"),role+" business request blocked by middleware");
            Check(!PasswordChangeGate.IsBlocked(Context(new AllowWhilePasswordChangeRequiredAttribute())),role+" change/logout remain accessible");
            Check(!PasswordChangeGate.IsBlocked(Context(new AllowAnonymousAttribute())),role+" anonymous login remains accessible");
            var normal=Context();normal.Items[PasswordChangeGate.StateKey]=false;
            Check(!PasswordChangeGate.IsBlocked(normal),role+" normal account passes password gate");
        }
    }
}
