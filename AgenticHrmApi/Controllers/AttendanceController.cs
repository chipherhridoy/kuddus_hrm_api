using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Authorization;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttendanceService _attendanceService;

    public AttendanceController(AppDbContext db, AttendanceService attendanceService)
    {
        _db = db;
        _attendanceService = attendanceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAttendance()
    {
        var records = await _db.AttendanceRecords
            .Include(a => a.User)
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.CheckInTime)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                UserName = a.User != null ? a.User.Name : "Unknown",
                UserRole = a.User != null ? a.User.Role : "Employee",
                UserDepartment = a.User != null ? a.User.Department : "General",
                Date = a.Date.ToString("yyyy-MM-dd"),
                CheckInTime = a.CheckInTime.ToString("hh:mm tt"),
                CheckOutTime = a.CheckOutTime.HasValue ? a.CheckOutTime.Value.ToString("hh:mm tt") : null,
                a.Status,
                a.Latitude,
                a.Longitude,
                a.Notes
            })
            .ToListAsync();

        return Ok(records);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserAttendance(int userId)
    {
        if (!User.IsInRole("Admin") && this.CurrentUserId() != userId) return Forbid();

        var records = await _db.AttendanceRecords
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Date)
            .ThenByDescending(a => a.CheckInTime)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                Date = a.Date.ToString("yyyy-MM-dd"),
                CheckInTime = a.CheckInTime.ToString("hh:mm tt"),
                CheckOutTime = a.CheckOutTime.HasValue ? a.CheckOutTime.Value.ToString("hh:mm tt") : null,
                a.Status,
                a.Latitude,
                a.Longitude,
                a.Notes
            })
            .ToListAsync();

        return Ok(records);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalEmployees = await _db.Users.CountAsync();
        var today = DateTime.UtcNow.Date;

        var todayRecords = await _db.AttendanceRecords
            .Where(a => a.Date.Date == today)
            .ToListAsync();

        // If no records for today yet (e.g. fresh startup), fallback to the latest seeded date so demo dashboard never displays broken zeroes
        if (todayRecords.Count == 0)
        {
            var latestDate = await _db.AttendanceRecords
                .OrderByDescending(a => a.Date)
                .Select(a => (DateTime?)a.Date.Date)
                .FirstOrDefaultAsync();

            if (latestDate.HasValue)
            {
                todayRecords = await _db.AttendanceRecords
                    .Where(a => a.Date.Date == latestDate.Value)
                    .ToListAsync();
            }
        }

        var presentToday = todayRecords.Count(a => a.Status == "Present" || a.Status == "Late");
        var lateToday = todayRecords.Count(a => a.Status == "Late");

        var onLeaveToday = await _db.LeaveRequests
            .Where(l => l.Status == "Approved")
            .CountAsync();

        return Ok(new
        {
            totalEmployees,
            presentToday,
            lateToday,
            onLeaveToday
        });
    }

    public class CheckInRequest
    {
        public int UserId { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Notes { get; set; }
    }

    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest req)
    {
        req.UserId = this.CurrentUserId();
        var outcome = await _attendanceService.CheckInAsync(req.UserId, req.Latitude, req.Longitude, req.Notes);
        if (!outcome.Success)
        {
            if (outcome.Record is not null)
            {
                return BadRequest(new { message = outcome.Message, record = outcome.Record });
            }
            return BadRequest(new { message = outcome.Message });
        }

        var user = await _db.Users.FindAsync(req.UserId);
        var record = outcome.Record!;

        return Ok(new
        {
            message = "Check-in successful",
            record = new
            {
                record.Id,
                record.UserId,
                UserName = user?.Name ?? "Unknown",
                Date = record.Date.ToString("yyyy-MM-dd"),
                CheckInTime = record.CheckInTime.ToString("hh:mm tt"),
                record.Status,
                record.Notes
            }
        });
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> CheckOut([FromBody] CheckInRequest req)
    {
        req.UserId = this.CurrentUserId();
        var outcome = await _attendanceService.CheckOutAsync(req.UserId, req.Notes);
        if (!outcome.Success)
        {
            return BadRequest(new { message = outcome.Message });
        }

        var record = outcome.Record!;
        return Ok(new
        {
            message = "Check-out successful",
            checkOutTime = record.CheckOutTime!.Value.ToString("hh:mm tt")
        });
    }

    public class OfflinePunchItem
    {
        public int UserId { get; set; }
        public string Type { get; set; } = "checkin"; // checkin or checkout
        public DateTime Timestamp { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Notes { get; set; }
    }

    public class OfflineSyncRequest
    {
        public List<OfflinePunchItem> Punches { get; set; } = new();
    }

    [HttpPost("offline-sync")]
    public async Task<IActionResult> OfflineSync([FromBody] OfflineSyncRequest req)
    {
        var currentUserId = this.CurrentUserId();
        var isAdmin = User.IsInRole("Admin");
        var results = new List<object>();

        foreach (var punch in req.Punches.OrderBy(p => p.Timestamp))
        {
            // Security: Normal users can only sync their own punches. Admins can sync anyone's punches.
            if (!isAdmin && punch.UserId != currentUserId)
            {
                results.Add(new { punch.UserId, punch.Type, success = false, message = "Forbidden" });
                continue;
            }

            AttendanceOutcome outcome;
            if (punch.Type.ToLower() == "checkout")
            {
                outcome = await _attendanceService.CheckOutAsync(punch.UserId, punch.Notes, punch.Timestamp);
            }
            else
            {
                outcome = await _attendanceService.CheckInAsync(punch.UserId, punch.Latitude, punch.Longitude, punch.Notes, punch.Timestamp);
            }

            results.Add(new { punch.UserId, punch.Type, success = outcome.Success, message = outcome.Message });
        }

        return Ok(new { message = "Offline sync processed", results });
    }
}
