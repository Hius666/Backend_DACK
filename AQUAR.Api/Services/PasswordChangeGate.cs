using Microsoft.AspNetCore.Authorization;

namespace AQUAR.Api;

[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute { }

public sealed class PasswordChangeGate(RequestDelegate next)
{
    public const string StateKey = "AQUAR.MustChangePassword";
    public static bool IsBlocked(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true &&
        context.Items.TryGetValue(StateKey, out var required) && required is true &&
        context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() == null &&
        context.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredAttribute>() == null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsBlocked(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "MUST_CHANGE_PASSWORD", message = "Bạn phải đổi mật khẩu trước khi sử dụng chức năng này." });
            return;
        }
        await next(context);
    }
}
