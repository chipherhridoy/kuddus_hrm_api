namespace AgenticHrmApi.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime? FaceEnrolledAt { get; set; }
    public string Role { get; set; } = "Employee"; // "Admin" or "Employee"
    public string Department { get; set; } = "General";
    public string Designation { get; set; } = "Staff";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<FaceTemplate> FaceTemplates { get; set; } = new List<FaceTemplate>();
}
