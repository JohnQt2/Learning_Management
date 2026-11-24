using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.AIModule.Implements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LMS.Core.Infrastructure.Adapters;
using Microsoft.AspNetCore.RateLimiting;
using LMS.Constants;
using LMS.CoreApplicationServices.AIModule.Abstracts;
namespace LMS.Core.API.Controllers;
[ApiController]
[Route("api/ai/questions")]
[Authorize(Roles = Roles.Lecturer)]
public sealed class AiQuestionsController : ControllerBase
{
    private readonly IAiQuestionService _service;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    public AiQuestionsController(IAiQuestionService service, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _service = service; _configuration = configuration; _environment = environment;
    }
    [HttpGet("status")]
    public IActionResult Status()
    {
        var mode = _configuration["Ai:Mode"] ?? "Disabled";
        var enabled = mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_configuration["Ai:GeminiApiKey"]);
        var mock = mode.Equals("Mock", StringComparison.OrdinalIgnoreCase) && _environment.IsDevelopment();
        var limits = AiGenerationLimits.From(_configuration);
        return Ok(new
        {
            enabled = enabled || mock, mode,
            model = enabled ? _configuration["Ai:GeminiModel"] ?? GeminiAiClient.DefaultModel : null,
            maxSourceCharacters = limits.MaxSourceCharacters,
            maxGenerationChunks = limits.MaxChunks, maxProviderRequests = limits.MaxRequests, maxRetries = limits.MaxRetries,
            timeoutSeconds = Math.Clamp(_configuration.GetValue("Ai:TimeoutSeconds", 45), 5, 120),
            generationTimeoutSeconds = limits.TotalTimeoutSeconds, minQuestionCount = 3, maxQuestionCount = 10,
            message = enabled ? "Gemini đã được cấu hình. Thao tác sinh câu hỏi gửi văn bản đã trích xuất tới Google Gemini."
                : mock ? "Chế độ mô phỏng chỉ dùng cho kiểm thử Development; chưa phải Gemini thật."
                : "AI chưa được cấu hình. Hãy soạn câu hỏi thủ công hoặc cấu hình Gemini trên Backend."
        });
    }
    [HttpPost("plan")]
    public async Task<IActionResult> Plan([FromBody] GenerateRequest r, CancellationToken ct)
        => Ok(await _service.PlanAsync(r, CurrentUser.Id(User), ct));
    [HttpPost("generate")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest r, CancellationToken ct)
    {
        var mode = _configuration["Ai:Mode"] ?? "Disabled";
        if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
            (mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase) &&
             string.IsNullOrWhiteSpace(_configuration["Ai:GeminiApiKey"])))
            return StatusCode(503, new { error = "AI chưa được cấu hình. Hãy soạn câu hỏi thủ công hoặc cấu hình Gemini trên Backend." });
        if (mode.Equals("Mock", StringComparison.OrdinalIgnoreCase) && !_environment.IsDevelopment())
            return StatusCode(503, new { error = "Chế độ AI mô phỏng không được dùng trên production." });
        return Ok(await _service.GenerateAsync(r, CurrentUser.Id(User), ct));
    }
}
