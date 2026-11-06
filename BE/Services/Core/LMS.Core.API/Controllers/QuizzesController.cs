using LMS.CoreApplicationServices.QuizModule.Dtos;
using LMS.CoreApplicationServices.QuizModule.Implements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace LMS.Core.API.Controllers;
[ApiController]
[Authorize]
public sealed class QuizzesController : ControllerBase
{
    private readonly QuizService _service;
    public QuizzesController(QuizService service) => _service = service;
    [HttpGet("api/courses/{courseId:int}/quizzes")]
    public async Task<IActionResult> List(int courseId, CancellationToken ct)
        => Ok(await _service.ListAsync(courseId, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpPost("api/quizzes")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Create([FromBody] QuizCreateRequest r, CancellationToken ct)
    {
        var quiz = await _service.CreateAsync(CurrentUser.Id(User), r, ct);
        return CreatedAtAction(nameof(Authoring), new { id = quiz.Id }, quiz);
    }
    [HttpGet("api/quizzes/{id:int}/authoring")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Authoring(int id, CancellationToken ct)
        => Ok(await _service.AuthoringAsync(id, CurrentUser.Id(User), ct));
    [HttpPut("api/quizzes/{id:int}")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Update(int id, [FromBody] QuizUpdateRequest r, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, CurrentUser.Id(User), r, ct));
    [HttpPost("api/quizzes/{id:int}/publish")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Publish(int id, [FromBody] QuizPublishRequest r, CancellationToken ct)
        => Ok(await _service.PublishAsync(id, CurrentUser.Id(User), r, ct));
    [HttpGet("api/quizzes/{id:int}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Take(int id, CancellationToken ct)
        => Ok(await _service.TakeAsync(id, CurrentUser.Id(User), ct));
    [HttpPost("api/quizzes/{id:int}/submit")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitRequest r, CancellationToken ct)
        => Ok(await _service.SubmitAsync(id, CurrentUser.Id(User), r, ct));
}
