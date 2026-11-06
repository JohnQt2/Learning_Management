using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.CourseModule.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Core.API.Controllers;
[ApiController]
[Route("api/courses")]
[Authorize]
public sealed class CoursesController : ControllerBase
{
    private readonly ICourseService _service;
    public CoursesController(ICourseService service) { _service = service; }
    [HttpGet]
    public async Task<ActionResult<List<CourseDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpPost]
    [Authorize(Roles = "Lecturer")]
    public async Task<ActionResult<CourseDto>> Create([FromBody] CourseCreateRequest r, CancellationToken ct)
    {
        var course = await _service.CreateAsync(CurrentUser.Id(User), r, ct);
        return CreatedAtAction(nameof(Get), new { id = course.Id }, course);
    }
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Update(int id, [FromBody] CourseUpdateRequest r, CancellationToken ct)
        => Ok(await _service.UpdateAsync(CurrentUser.Id(User), id, r, ct));
    [HttpGet("{id:int}/students")]
    [Authorize(Roles = "Lecturer")]
    public async Task<IActionResult> Students(int id, CancellationToken ct)
        => Ok(await _service.StudentsAsync(CurrentUser.Id(User), id, ct));
    [HttpPost("{id:int}/enroll")]
    [Authorize(Roles = "Student")]
    public async Task<ActionResult<EnrollmentDto>> Enroll(int id, [FromBody] EnrollmentRequest r, CancellationToken ct)
        => Ok(await _service.EnrollAsync(CurrentUser.Id(User), id, r, ct));
    [HttpPost("enroll")]
    [Authorize(Roles = "Student")]
    public async Task<ActionResult<EnrollmentDto>> EnrollByCode([FromBody] EnrollmentRequest r, CancellationToken ct)
        => Ok(await _service.EnrollByCodeAsync(CurrentUser.Id(User), r, ct));
    [HttpGet("{id:int}/topics")]
    public async Task<IActionResult> Topics(int id, CancellationToken ct)
        => Ok(await _service.ListTopicsAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpPost("{id:int}/topics")]
    [Authorize(Roles = "Lecturer")]
    public async Task<ActionResult<TopicDto>> CreateTopic(int id, [FromBody] TopicRequest r, CancellationToken ct)
    {
        var topic = await _service.CreateTopicAsync(id, CurrentUser.Id(User), r, ct);
        return CreatedAtAction(nameof(Topics), new { id }, topic);
    }
    [HttpPut("{id:int}/topics/{topicId:int}")]
    [Authorize(Roles = "Lecturer")]
    public async Task<ActionResult<TopicDto>> UpdateTopic(int id, int topicId, [FromBody] TopicRequest r, CancellationToken ct)
        => Ok(await _service.UpdateTopicAsync(id, topicId, CurrentUser.Id(User), r, ct));
}
