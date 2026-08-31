using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace AgenticHrmApi.Controllers;

public static class ControllerExtensions
{
    public static int CurrentUserId(this ControllerBase c) =>
        int.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
