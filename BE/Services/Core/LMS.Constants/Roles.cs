namespace LMS.Constants;

/// <summary>Ba vai trò cố định của hệ thống. Giá trị chuỗi trùng với cột Users.Role và claim Role trong JWT.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Lecturer = "Lecturer";
    public const string Student = "Student";

    public static bool IsValid(string? role) => role is Admin or Lecturer or Student;
}
