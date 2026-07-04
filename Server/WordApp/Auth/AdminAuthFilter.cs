using Microsoft.AspNetCore.Mvc.Filters;

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

        if (string.IsNullOrEmpty(expected) || provided != expected)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedResult();
            return;
        }

        await next();
    }
}
