using System;
using System.Linq;
using System.Threading.Tasks;
using AgenticHrmApi.Data;
using AgenticHrmApi.Services.Face;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/face/sync")]
[Authorize]
public class FaceSyncController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TemplateCipher _cipher;

    public FaceSyncController(AppDbContext db, TemplateCipher cipher)
    {
        _db = db;
        _cipher = cipher;
    }

    [HttpGet]
    public async Task<IActionResult> GetUpdates([FromQuery] DateTime? since)
    {
        var query = _db.FaceTemplates.AsQueryable();

        if (since.HasValue)
        {
            query = query.Where(t => t.CreatedAt > since.Value || (t.UpdatedAt.HasValue && t.UpdatedAt.Value > since.Value));
        }

        var templates = await query.ToListAsync();

        var result = templates.Select(t => new
        {
            t.Id,
            t.UserId,
            t.Pose,
            t.IsActive,
            Embedding = t.IsActive ? _cipher.Decrypt(t.EncryptedEmbedding, t.Nonce, t.Tag) : null,
            Timestamp = t.UpdatedAt ?? t.CreatedAt
        }).ToList();

        return Ok(result);
    }
}
