using LMS.Core.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LMS.Constants;

namespace LMS.Core.Infrastructure.Persistence;

public static class DemoSeeder
{
    public static async Task SeedAsync(LmsDbContext db, string password, CancellationToken ct = default)
    {
        ValidatePassword(password);
        if (await db.Users.AnyAsync(ct)) return;
        var hasher = new PasswordHasher<User>();
        foreach (var (username, role, name) in new[]
        {
            ("lecturer", Roles.Lecturer, "Giảng viên Development"),
            ("student", Roles.Student, "Sinh viên Development"),
            ("admin", Roles.Admin, "Quản trị Development")
        })
        {
            var user = new User { Username = username, Role = role, FullName = name };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task BootstrapAdminAsync(LmsDbContext db, string username, string password,
        string fullName, CancellationToken ct = default)
    {
        ValidatePassword(password);
        username = username.Trim().ToLowerInvariant();
        if (username.Length is < 3 or > 80 || username.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.' && c != '-'))
            throw new InvalidOperationException("Bootstrap:AdminUsername must have 3-80 letters, digits, '.', '_' or '-'.");
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 150)
            throw new InvalidOperationException("Bootstrap:AdminFullName must have 1-150 characters.");
        // Never promote or overwrite an existing account; provisioning only creates the first admin.
        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin, ct)) return;
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new InvalidOperationException("Bootstrap admin username is already in use; no account was changed.");
        var user = new User { Username = username, Role = Roles.Admin, FullName = fullName.Trim() };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 10 or > 128 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(c => !char.IsLetterOrDigit(c)))
            throw new InvalidOperationException("Seed/bootstrap password must have 10-128 characters including upper/lower case, digit and symbol; configure it through environment variables or user secrets.");
    }
}
