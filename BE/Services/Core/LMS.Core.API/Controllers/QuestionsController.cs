using LMS.CoreApplicationServices.QuestionModule.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LMS.Constants;
using LMS.CoreApplicationServices.QuestionModule.Abstracts;

namespace LMS.Core.API.Controllers;
[ApiController]
[Authorize(Roles = Roles.Lecturer)]
public sealed class QuestionsController : ControllerBase
{
    private readonly IQuestionService _questions;
    public QuestionsController(IQuestionService questions) => _questions = questions;
    [HttpGet("api/courses/{courseId:int}/questions")]
    public async Task<IActionResult> List(int courseId, CancellationToken ct)
        => Ok(await _questions.ListAsync(courseId, CurrentUser.Id(User), ct));
    [HttpPost("api/questions")]
    public async Task<IActionResult> Create([FromBody] QuestionCreateRequest r, CancellationToken ct)
    {
        var question = await _questions.CreateAsync(CurrentUser.Id(User), r, ct);
        return Created($"/api/courses/{question.CourseId}/questions", question);
    }
    [HttpPut("api/questions/{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] QuestionEditRequest r, CancellationToken ct)
        => Ok(await _questions.EditAsync(id, CurrentUser.Id(User), r, ct));
    [HttpPost("api/questions/{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
        => Ok(await _questions.ApproveAsync(id, CurrentUser.Id(User), ct));
}
