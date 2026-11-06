using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AuthenticationModule.Abstracts;
using LMS.CoreApplicationServices.AuthenticationModule.Dtos;
using LMS.CoreApplicationServices.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.AuthenticationModule.Implements;
public sealed class AuthService : IAuthService
{
    private readonly LmsDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    public AuthService(LmsDbContext db, IPasswordHasher<User> hasher) { _db = db; _hasher = hasher; }
    public async Task<User> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 80 || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 128)
            throw new AppException(400, "Cần nhập tài khoản và mật khẩu");
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username.Trim().ToLowerInvariant(), ct);
        if (user == null || user.IsLocked || !VerifyPassword(user, request.Password))
            throw new AppException(401, "Tài khoản hoặc mật khẩu không hợp lệ");
        return user;
    }
    public async Task<User> RegisterStudentAsync(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim().ToLowerInvariant() ?? "";
        var name = request.FullName?.Trim() ?? "";
        var password = request.Password ?? "";
        if (username.Length is < 3 or > 40 || !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9_.-]+$") || name.Length is < 2 or > 100)
            throw new AppException(400, "Tên đăng nhập (3-40 ký tự a-z, 0-9) hoặc họ tên không hợp lệ");
        if (password.Length < 8 || password.Length > 128) throw new AppException(400, "Mật khẩu cần từ 8 đến 128 ký tự");
        if (await _db.Users.AnyAsync(u => u.Username == username, ct)) throw new AppException(409, "Tên đăng nhập đã tồn tại");
        var email = ProfileValidation.NormalizeEmail(request.Email);
        var user = new User { Username = username, FullName = name, Role = "Student", Email = email };
        user.PasswordHash = _hasher.HashPassword(user, password);
        _db.Users.Add(user);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            _db.Entry(user).State = EntityState.Detached;
            throw new AppException(409, "Tên đăng nhập đã tồn tại");
        }
        return user;
    }

    public async Task<AccountProfileDto> ProfileAsync(int userId, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        var courses = user.Role == "Lecturer"
            ? await _db.Courses.AsNoTracking().Where(c => c.LecturerId == userId).OrderBy(c => c.Title).ThenBy(c => c.Id)
                .Select(c => new ProfileCourseDto(c.Id, c.Code, c.Title, "Teaching", null)).ToListAsync(ct)
            : user.Role == "Student"
                ? await _db.Enrollments.AsNoTracking().Where(e => e.StudentId == userId).OrderBy(e => e.Course.Title).ThenBy(e => e.CourseId)
                    .Select(e => new ProfileCourseDto(e.CourseId, e.Course.Code, e.Course.Title, "Enrolled", e.EnrolledAt)).ToListAsync(ct)
                : [];
        return new(user.Id, user.Username, user.FullName, user.Role, user.IsLocked, user.Email, user.CreatedAt, courses);
    }

    public async Task<AccountProfileDto> UpdateProfileAsync(int userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        var fullName = request.FullName?.Trim() ?? "";
        if (fullName.Length is < 2 or > 100)
            throw new AppException(400, "Họ tên cần từ 2 đến 100 ký tự");
        var email = ProfileValidation.NormalizeEmail(request.Email);
        // The request intentionally exposes only self-editable profile fields.
        user.FullName = fullName;
        user.Email = email;
        await SaveAccountChangeAsync(ct);
        return await ProfileAsync(userId, ct);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128 || !VerifyPassword(user, request.CurrentPassword))
            throw new AppException(400, "Mật khẩu hiện tại không đúng");
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length is < 8 or > 128)
            throw new AppException(400, "Mật khẩu mới cần từ 8 đến 128 ký tự");
        if (request.CurrentPassword == request.NewPassword)
            throw new AppException(400, "Mật khẩu mới phải khác mật khẩu hiện tại");
        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.SecurityVersion++;
        await SaveAccountChangeAsync(ct);
    }

    public async Task LogoutAsync(int userId, CancellationToken ct)
    {
        var user = await RequireActiveUserAsync(userId, ct);
        // Revokes every token issued for this account, including the current session.
        user.SecurityVersion++;
        await SaveAccountChangeAsync(ct);
    }

    private async Task<User> RequireActiveUserAsync(int userId, CancellationToken ct)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null || user.IsLocked) throw new AppException(401, "Phiên đăng nhập không còn hợp lệ");
        return user;
    }

    private bool VerifyPassword(User user, string password)
    {
        try { return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    private async Task SaveAccountChangeAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(409, "Tài khoản vừa thay đổi ở phiên khác; hãy đăng nhập lại");
        }
    }
}
