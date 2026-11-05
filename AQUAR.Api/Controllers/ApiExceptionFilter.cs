using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AQUAR.Api;

// Expected validation/access errors are HTTP responses, not server failures.
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            context.Result = new ConflictObjectResult(new { message = "Tài khoản vừa được cập nhật ở phiên khác. Vui lòng đăng nhập lại và thử lại." });
            context.ExceptionHandled = true;
            return;
        }
        if (context.Exception is not ApiException error) return;
        context.Result = new ObjectResult(new { message = error.Message }) { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}
