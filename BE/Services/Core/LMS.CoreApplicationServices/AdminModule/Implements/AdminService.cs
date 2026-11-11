using LMS.Core.Infrastructure.Persistence;
using LMS.Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using LMS.CoreApplicationServices.AdminModule.Dtos;
using LMS.CoreApplicationServices.Common;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.AdminModule.Implements;
public sealed class AdminService
{
    private readonly LmsDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    public AdminService(LmsDbContext db, IPasswordHasher<User> hasher) { _db = db; _hasher = hasher; }
    public Task<object> ListAsync(int page, int pageSize, CancellationToken ct)
        => ListAsync(page, pageSize, null, null, null, ct);

    public async Task<object> ListAsync(int page, int pageSize, string? search, string? role, bool? isLocked, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 1000000); pageSize = Math.Clamp(pageSize, 1, 100);
        var term = search?.Trim().ToLowerInvariant() ?? "";
        if (term.Length > 150) throw new AppException(400, "Từ khóa tìm kiếm tối đa 150 ký tự");
        role = role?.Trim();
        if (!string.IsNullOrEmpty(role) && role is not ("Admin" or "Lecturer" or "Student"))
            throw new AppException(400, "Vai trò phải là Admin, Lecturer hoặc Student");
        IQueryable<User> query = _db.Users.AsNoTracking();
        if (term.Length > 0) query = query.Where(u => u.Username.ToLower().Contains(term) ||
            u.FullName.ToLower().Contains(term) || (u.Email != null && u.Email.ToLower().Contains(term)));
        if (!string.IsNullOrEmpty(role)) query = query.Where(u => u.Role == role);
        if (isLocked.HasValue) query = query.Where(u => u.IsLocked == isLocked.Value);
        return new { Total = await query.CountAsync(ct), Items = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsLocked, u.Email, u.CreatedAt)).ToListAsync(ct), Page = page, PageSize = pageSize };
    }
    public async Task<UserDto> GetAsync(int userId, CancellationToken ct)
        => await _db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsLocked, u.Email, u.CreatedAt)).SingleOrDefaultAsync(ct)
            ?? throw new AppException(404, "User không tồn tại");
    public async Task<UserDto> LockAsync(int targetId, int actorId, LockRequest request, CancellationToken ct)
    {
        if (targetId == actorId && request.IsLocked) throw new AppException(400, "Admin không được tự khóa chính mình");
        var user = await _db.Users.FindAsync(new object[] { targetId }, ct) ?? throw new AppException(404, "User không tồn tại");
        if (user.IsLocked != request.IsLocked)
        {
            user.IsLocked = request.IsLocked;
            user.SecurityVersion++;
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new AppException(409, "Tài khoản vừa thay đổi; hãy tải lại danh sách"); }
        }
        return new(user.Id, user.Username, user.FullName, user.Role, user.IsLocked, user.Email, user.CreatedAt);
    }
    public async Task<UserDto> CreateLecturerAsync(CreateLecturerRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim().ToLowerInvariant() ?? "";
        var name = request.FullName?.Trim() ?? "";
        if (username.Length is < 3 or > 40 || !System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9_.-]+$") || name.Length is < 2 or > 100)
            throw new AppException(400, "Thông tin tài khoản không hợp lệ");
        if (request.Password == null || request.Password.Length is < 8 or > 128)
            throw new AppException(400, "Mật khẩu cần từ 8-128 ký tự");
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            throw new AppException(409, "Username đã tồn tại");
        var email = ProfileValidation.NormalizeEmail(request.Email);
        var u = new User { Username = username, FullName = name, Role = "Lecturer", Email = email };
        u.PasswordHash = _hasher.HashPassword(u, request.Password);
        _db.Users.Add(u);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            _db.Entry(u).State = EntityState.Detached;
            throw new AppException(409, "Username đã tồn tại");
        }
        return new(u.Id, u.Username, u.FullName, u.Role, u.IsLocked, u.Email, u.CreatedAt);
    }
}
