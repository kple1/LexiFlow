using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace WordApp.Auth;

// Guards /admin/api/* routes with a shared secret sent as the X-Admin-Token header.
public class AdminAuthFilter : IAsyncActionFilter
{
    private readonly IConfiguration _cfg;
    public AdminAuthFilter(IConfiguration cfg) => _cfg = cfg;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = _cfg["Admin:Token"];
        var provided = context.HttpContext.Request.Headers["X-Admin-Token"].ToString();

        if (string.IsNullOrEmpty(expected) || expected.Length < 32 || provided.Length > 512 ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }

        await next();
    }
}
