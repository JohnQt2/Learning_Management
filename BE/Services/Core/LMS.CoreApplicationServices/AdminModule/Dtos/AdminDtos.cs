namespace LMS.CoreApplicationServices.AdminModule.Dtos;
public record CreateLecturerRequest(string Username, string FullName, string Password, string? Email = null);
public record LockRequest(bool IsLocked);
public record UserDto(int Id, string Username, string FullName, string Role, bool IsLocked,
    string? Email = null, DateTime? CreatedAt = null);
