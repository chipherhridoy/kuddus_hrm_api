using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace AgenticHrmApi.Controllers;

public static class ControllerExtensions
{
    public static int CurrentUserId(this ControllerBase c)
    {
        var val = c.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? c.User.FindFirstValue("sub")
               ?? c.User.FindFirstValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
               ?? c.User.Claims.FirstOrDefault(x => x.Type == "sub" || x.Type.EndsWith("nameidentifier"))?.Value;
        if (int.TryParse(val, out var id)) return id;
        return 1;
    }
}
