using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentController(
    ConversationService conversation,
    GroqApiService groq,
    AgenticHrmApi.Data.AppDbContext db) : ControllerBase
{
    [HttpPost("converse")]
    public async Task<IActionResult> Converse([FromForm] ConverseRequest req, CancellationToken ct = default)
    {
        req.UserId = this.CurrentUserId();
        if (req.Audio is null && string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { message = "Provide either audio or text." });

        if (req.Audio is { Length: > 0 } && string.IsNullOrWhiteSpace(req.Text))
        {
            // The recogniser is biased toward the speaker's own name and
            // department, which is what stops "Kuddus" arriving as one of
            // nineteen spellings.
            var speaker = await db.Users.FindAsync([req.UserId], ct);
            var hints = new TranscriptionHints(
                speaker?.Name ?? "the user",
                speaker?.Department ?? "the company");

            await using var stream = req.Audio.OpenReadStream();
            var transcription = await groq.TranscribeAudioAsync(stream, req.Audio.FileName, hints, ct);

            // A hallucinated transcript is worse than none: it makes Kuddus
            // act on words nobody said. An empty Text routes into
            // ConversationService's existing "didn't catch that" path.
            req.Text = transcription.IsLikelyHallucination ? string.Empty : transcription.Text;
        }

        return Ok(await conversation.ProcessAsync(req, this.User, ct));
    }

    /// Retained so the existing mobile build keeps working during migration.
    [HttpPost("voice")]
    public Task<IActionResult> ProcessVoice([FromForm] IFormFile audio,
        CancellationToken ct = default) =>
        Converse(new ConverseRequest { Audio = audio }, ct);
}
