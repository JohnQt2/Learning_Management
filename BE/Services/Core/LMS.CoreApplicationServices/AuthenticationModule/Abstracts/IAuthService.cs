using LMS.Core.Domain.Entities;
using LMS.CoreApplicationServices.AuthenticationModule.Dtos;
namespace LMS.CoreApplicationServices.AuthenticationModule.Abstracts;
public interface IAuthService
{
    Task<User> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<User> RegisterStudentAsync(RegisterRequest request, CancellationToken ct);
    Task<AccountProfileDto> ProfileAsync(int userId, CancellationToken ct);
    Task<AccountProfileDto> UpdateProfileAsync(int userId, UpdateProfileRequest request, CancellationToken ct);
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct);
    Task LogoutAsync(int userId, CancellationToken ct);
    /// <summary>Kiểm tra JWT còn hợp lệ theo trạng thái tài khoản hiện tại (gọi khi xác thực mỗi request).</summary>
    Task<bool> IsSessionValidAsync(int userId, string? role, int securityVersion, CancellationToken ct);
}
