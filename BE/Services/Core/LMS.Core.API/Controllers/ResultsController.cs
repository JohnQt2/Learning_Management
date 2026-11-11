using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LMS.Constants;
using LMS.CoreApplicationServices.ResultModule.Abstracts;
namespace LMS.Core.API.Controllers;
[ApiController]
[Authorize]
public sealed class ResultsController : ControllerBase
{
    private readonly IResultService _service;
    public ResultsController(IResultService service) => _service = service;
    [HttpGet("api/courses/{courseId:int}/attempts")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> CourseAttempts(int courseId, CancellationToken ct)
        => Ok(await _service.CourseAttemptsAsync(courseId, CurrentUser.Id(User), ct));
    [HttpGet("api/courses/{courseId:int}/statistics")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> CourseStatistics(int courseId, CancellationToken ct)
        => Ok(await _service.CourseStatisticsAsync(courseId, CurrentUser.Id(User), ct));
    [HttpGet("api/attempts/{id:int}/result")]
    public async Task<IActionResult> Result(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpGet("api/students/me/attempts")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> MyAttempts(CancellationToken ct)
        => Ok(await _service.MyAttemptsAsync(CurrentUser.Id(User), ct));
    [HttpGet("api/students/me/recommendations")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Recommendations(CancellationToken ct)
        => Ok(await _service.MyRecommendationsAsync(CurrentUser.Id(User), ct));
    [HttpGet("api/students/me/dashboard")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
        => Ok(await _service.DashboardAsync(CurrentUser.Id(User), ct));
}
