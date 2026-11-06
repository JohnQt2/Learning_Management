using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LMS.CoreApplicationServices.AuthenticationModule.Abstracts;
using LMS.CoreApplicationServices.AuthenticationModule.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace LMS.Core.API.Controllers;
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly TokenConfig _tokens;
    public AuthController(IAuthService auth, TokenConfig tokens) { _auth = auth; _tokens = tokens; }
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest input, CancellationToken ct)
    {
        var user = await _auth.RegisterStudentAsync(input, ct);
        return StatusCode(StatusCodes.Status201Created, CreateLoginResponse(user));
    }
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
        => Ok(await _auth.ProfileAsync(CurrentUser.Id(User), ct));

    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
        => Ok(await _auth.UpdateProfileAsync(CurrentUser.Id(User), request, ct));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(CurrentUser.Id(User), request, ct);
        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _auth.LogoutAsync(CurrentUser.Id(User), ct);
        return NoContent();
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest input, CancellationToken ct)
    {
        var user = await _auth.LoginAsync(input, ct);
        return Ok(CreateLoginResponse(user));
    }
    private LoginResponse CreateLoginResponse(LMS.Core.Domain.Entities.User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("sv", user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        var expiresAt = DateTime.UtcNow.AddHours(8);
        var token = new JwtSecurityToken(_tokens.Issuer, _tokens.Audience, claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(_tokens.Key, SecurityAlgorithms.HmacSha256));
        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Id, user.Role, user.FullName, expiresAt);
    }
}
