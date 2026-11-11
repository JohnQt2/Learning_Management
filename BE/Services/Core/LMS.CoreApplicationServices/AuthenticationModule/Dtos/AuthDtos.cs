namespace LMS.CoreApplicationServices.AuthenticationModule.Dtos;
public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string FullName, string Password, string? Email = null);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UpdateProfileRequest(string FullName, string? Email);
public record ProfileCourseDto(int CourseId, string Code, string Title, string Relationship, DateTime? EnrolledAt);
public record AccountProfileDto(int UserId, string Username, string FullName, string Role, bool IsLocked,
    string? Email = null, DateTime? CreatedAt = null, List<ProfileCourseDto>? Courses = null);
public record LoginResponse(string AccessToken, int UserId, string Role, string DisplayName, DateTime? ExpiresAtUtc = null);
