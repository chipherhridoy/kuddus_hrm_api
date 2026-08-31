using AgenticHrmApi.Contracts;
using AgenticHrmApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AgenticHrmApi.Tests;

public class AgentEndpointTests
{
    [Fact]
    public async Task Converse_rejects_a_request_with_neither_audio_nor_text()
    {
        var controller = new AgentController(null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "3")
                    ], "TestAuthType"))
                }
            }
        };
        var result = await controller.Converse(new ConverseRequest { UserId = 3 });
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
