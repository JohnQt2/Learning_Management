using LMS.CoreApplicationServices.AdminModule.Dtos;
using LMS.CoreApplicationServices.AdminModule.Implements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Core.API.Controllers;
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AdminService _service;
    public AdminController(AdminService service) => _service = service;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string? role = null, [FromQuery] bool? isLocked = null, CancellationToken ct = default)
        => Ok(await _service.ListAsync(page, pageSize, search, role, isLocked, ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));
    [HttpPost("lecturers")]
    public async Task<IActionResult> CreateLecturer([FromBody] CreateLecturerRequest request, CancellationToken ct)
    {
        var user = await _service.CreateLecturerAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }
    [HttpPatch("{id:int}/lock")]
    public async Task<IActionResult> Lock(int id, [FromBody] LockRequest r, CancellationToken ct)
        => Ok(await _service.LockAsync(id, CurrentUser.Id(User), r, ct));
}
