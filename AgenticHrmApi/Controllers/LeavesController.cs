using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Authorization;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeavesController : ControllerBase
{
    private readonly AppDbContext _db;

    public LeavesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllLeaves()
    {
        var leaves = await _db.LeaveRequests
            .Include(l => l.User)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id,
                l.UserId,
                UserName = l.User != null ? l.User.Name : "Unknown",
                UserRole = l.User != null ? l.User.Role : "Employee",
                UserDepartment = l.User != null ? l.User.Department : "General",
                StartDate = l.StartDate.ToString("yyyy-MM-dd"),
                EndDate = l.EndDate.ToString("yyyy-MM-dd"),
                l.Reason,
                l.Status,
                CreatedAt = l.CreatedAt.ToString("yyyy-MM-dd hh:mm tt")
            })
            .ToListAsync();

        return Ok(leaves);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserLeaves(int userId)
    {
        if (!User.IsInRole("Admin") && this.CurrentUserId() != userId) return Forbid();

        var leaves = await _db.LeaveRequests
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id,
                l.UserId,
                StartDate = l.StartDate.ToString("yyyy-MM-dd"),
                EndDate = l.EndDate.ToString("yyyy-MM-dd"),
                l.Reason,
                l.Status,
                CreatedAt = l.CreatedAt.ToString("yyyy-MM-dd hh:mm tt")
            })
            .ToListAsync();

        return Ok(leaves);
    }

    public class CreateLeaveRequest
    {
        public int UserId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CreateLeave([FromBody] CreateLeaveRequest req)
    {
        req.UserId = this.CurrentUserId();
        var user = await _db.Users.FindAsync(req.UserId);
        if (user == null) return NotFound(new { message = "User not found" });

        var leave = new LeaveRequest
        {
            UserId = req.UserId,
            StartDate = DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc),
            EndDate = DateTime.SpecifyKind(req.EndDate, DateTimeKind.Utc),
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? "No reason specified" : req.Reason,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.LeaveRequests.Add(leave);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Leave application submitted successfully",
            leave = new
            {
                leave.Id,
                leave.UserId,
                UserName = user.Name,
                StartDate = leave.StartDate.ToString("yyyy-MM-dd"),
                EndDate = leave.EndDate.ToString("yyyy-MM-dd"),
                leave.Reason,
                leave.Status
            }
        });
    }

    public class UpdateLeaveStatusRequest
    {
        public string Status { get; set; } = "Approved"; // Approved, Rejected, Pending
    }

    [HttpPatch("{id}/status")]
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateLeaveStatus(int id, [FromBody] UpdateLeaveStatusRequest req)
    {
        var leave = await _db.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
        if (leave == null) return NotFound(new { message = "Leave request not found" });

        if (req.Status != "Approved" && req.Status != "Rejected" && req.Status != "Pending")
        {
            return BadRequest(new { message = "Invalid status. Allowed values: Pending, Approved, Rejected" });
        }

        leave.Status = req.Status;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = $"Leave request #{id} updated to {req.Status}",
            leave = new
            {
                leave.Id,
                leave.UserId,
                UserName = leave.User?.Name,
                StartDate = leave.StartDate.ToString("yyyy-MM-dd"),
                EndDate = leave.EndDate.ToString("yyyy-MM-dd"),
                leave.Reason,
                leave.Status
            }
        });
    }
}
