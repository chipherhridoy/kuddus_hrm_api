using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentController(ConversationService conversation, GroqApiService groq) : ControllerBase
{
    [HttpPost("converse")]
    public async Task<IActionResult> Converse([FromForm] ConverseRequest req, CancellationToken ct = default)
    {
        req.UserId = this.CurrentUserId();
        if (req.Audio is null && string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { message = "Provide either audio or text." });

        if (req.Audio is { Length: > 0 } && string.IsNullOrWhiteSpace(req.Text))
        {
            await using var stream = req.Audio.OpenReadStream();
            req.Text = await groq.TranscribeAudioAsync(stream, req.Audio.FileName);
        }

        return Ok(await conversation.ProcessAsync(req, this.User, ct));
    }

    /// Retained so the existing mobile build keeps working during migration.
    [HttpPost("voice")]
    public Task<IActionResult> ProcessVoice([FromForm] IFormFile audio,
        CancellationToken ct = default) =>
        Converse(new ConverseRequest { Audio = audio }, ct);
}
