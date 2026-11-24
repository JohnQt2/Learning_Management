using LMS.CoreApplicationServices.QuizModule.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LMS.Constants;
using LMS.CoreApplicationServices.QuizModule.Abstracts;
namespace LMS.Core.API.Controllers;
[ApiController]
[Authorize]
public sealed class QuizzesController : ControllerBase
{
    private readonly IQuizService _service;
    public QuizzesController(IQuizService service) => _service = service;
    [HttpGet("api/courses/{courseId:int}/quizzes")]
    public async Task<IActionResult> List(int courseId, CancellationToken ct)
        => Ok(await _service.ListAsync(courseId, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpPost("api/quizzes")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Create([FromBody] QuizCreateRequest r, CancellationToken ct)
    {
        var quiz = await _service.CreateAsync(CurrentUser.Id(User), r, ct);
        return CreatedAtAction(nameof(Authoring), new { id = quiz.Id }, quiz);
    }
    [HttpGet("api/quizzes/{id:int}/authoring")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Authoring(int id, CancellationToken ct)
        => Ok(await _service.AuthoringAsync(id, CurrentUser.Id(User), ct));
    [HttpPut("api/quizzes/{id:int}")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Update(int id, [FromBody] QuizUpdateRequest r, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, CurrentUser.Id(User), r, ct));
    [HttpPost("api/quizzes/{id:int}/publish")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Publish(int id, [FromBody] QuizPublishRequest r, CancellationToken ct)
        => Ok(await _service.PublishAsync(id, CurrentUser.Id(User), r, ct));
    [HttpGet("api/quizzes/{id:int}")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Take(int id, CancellationToken ct)
        => Ok(await _service.TakeAsync(id, CurrentUser.Id(User), ct));
    [HttpPost("api/quizzes/{id:int}/submit")]
    [Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Submit(int id, [FromBody] SubmitRequest r, CancellationToken ct)
        => Ok(await _service.SubmitAsync(id, CurrentUser.Id(User), r, ct));
}
