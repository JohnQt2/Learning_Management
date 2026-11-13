using System.Diagnostics;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.QuestionModule.Dtos;
using LMS.CoreApplicationServices.QuestionModule.Implements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LMS.CoreApplicationServices.AIModule.Implements;

public sealed class AiQuestionService
{
    private readonly LmsDbContext _db;
    private readonly IAIClient _client;
    private readonly ICourseService _courses;
    private readonly ILogger<AiQuestionService>? _log;
    private readonly AiGenerationLimits _limits;
    public AiQuestionService(LmsDbContext db, IAIClient client, ICourseService courses,
        ILogger<AiQuestionService>? log = null, IConfiguration? configuration = null)
    {
        _db = db; _client = client; _courses = courses; _log = log;
        _limits = AiGenerationLimits.From(configuration ?? new ConfigurationBuilder().Build());
    }

    public async Task<AiGenerationPlanDto> PlanAsync(GenerateRequest request, int lecturerId, CancellationToken ct)
    {
        var material = await RequireMaterialAsync(request, lecturerId, ct);
        return AiGenerationPlanner.Build(material.Id, material.ExtractedText, request.Count, _limits);
    }

    public async Task<List<QuestionDto>> GenerateAsync(GenerateRequest request, int lecturerId, CancellationToken ct)
    {
        var material = await RequireMaterialAsync(request, lecturerId, ct);
        var plan = AiGenerationPlanner.Build(material.Id, material.ExtractedText, request.Count, _limits);
        var budget = new AiRequestBudget(_limits.MaxRequests);
        var timer = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_limits.TotalTimeoutSeconds));
        var questions = new List<Question>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var chunk in plan.Chunks)
            {
                var source = material.ExtractedText[chunk.StartCharacter..chunk.EndCharacter];
                var json = await _client.GenerateJsonAsync(source, chunk.QuestionCount, request.Difficulty, budget, deadline.Token);
                foreach (var row in QuestionJsonValidator.Parse(json, chunk.QuestionCount))
                {
                    if (!seen.Add(row.Content.Trim())) continue;
                    questions.Add(new Question
                    {
                        CourseId = material.CourseId, TopicId = material.TopicId, MaterialId = material.Id,
                        Content = row.Content.Trim(), Explanation = row.Explanation, Source = "AI", Status = "Draft",
                        Difficulty = request.Difficulty, SourceChunkIndex = chunk.Index,
                        SourceStartCharacter = chunk.StartCharacter, SourceEndCharacter = chunk.EndCharacter,
                        Options = row.Options.Select(o => new AnswerOption { Content = o.Content, IsCorrect = o.IsCorrect }).ToList()
                    });
                }
            }
            if (questions.Count == 0) throw new AppException(422, "AI không trả câu hỏi hợp lệ");
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (AiClientException ex)
        {
            await LogFailureAsync(material.Id, request.Count, plan.ChunkCount, budget.Used, timer.ElapsedMilliseconds,
                ex.ProviderStatusCode is int status ? $"AI provider request failed (HTTP {status})" : "AI provider request failed", ct);
            throw new AppException(ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await LogFailureAsync(material.Id, request.Count, plan.ChunkCount, budget.Used, timer.ElapsedMilliseconds, "AI generation deadline exceeded", ct);
            throw new AppException(504, "AI phản hồi quá thời gian; hãy thử lại sau hoặc soạn câu hỏi thủ công");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogFailureAsync(material.Id, request.Count, plan.ChunkCount, budget.Used, timer.ElapsedMilliseconds, "AI response failed validation", ct);
            if (ex is AppException) throw;
            throw new AppException(422, "Không thể sinh câu hỏi từ AI");
        }

        // Commit only after every chunk succeeds; detect deletion during provider calls.
        await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            _db.Entry(material).Property(m => m.IsDeleted).IsModified = true;
            _db.Questions.AddRange(questions);
            _db.AIGenerationLogs.Add(new AIGenerationLog
            {
                MaterialId = material.Id, Status = "Success", RequestedQuestionCount = request.Count,
                GeneratedQuestionCount = questions.Count, ChunkCount = plan.ChunkCount,
                ProviderRequestCount = budget.Used, DurationMilliseconds = timer.ElapsedMilliseconds
            });
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw new AppException(409, "Học liệu đã thay đổi hoặc bị xóa; hãy tải lại danh sách");
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        return questions.Select(QuestionService.Map).ToList();
    }

    private async Task<LearningMaterial> RequireMaterialAsync(GenerateRequest request, int lecturerId, CancellationToken ct)
    {
        if (request.Count is < 3 or > 10) throw new AppException(400, "Số câu hỏi phải từ 3 đến 10");
        if (request.Difficulty is not ("Easy" or "Medium" or "Hard")) throw new AppException(400, "Độ khó phải là Easy, Medium hoặc Hard");
        var material = await _db.LearningMaterials.SingleOrDefaultAsync(m => m.Id == request.MaterialId && !m.IsDeleted, ct)
            ?? throw new AppException(404, "Không tìm thấy học liệu");
        await _courses.RequireOwnerAsync(material.CourseId, lecturerId, ct);
        var extension = Path.GetExtension(material.OriginalFileName).ToLowerInvariant();
        if (extension is not (".txt" or ".md" or ".pdf" or ".docx" or ".pptx") ||
            string.IsNullOrWhiteSpace(material.ExtractedText) || material.ExtractionStatus is not ("Legacy" or "Extracted"))
            throw new AppException(400, "Học liệu cần có văn bản đã trích xuất hợp lệ; PDF scan hoặc slide chỉ có ảnh cần OCR");
        return material;
    }

    private async Task LogFailureAsync(int materialId, int requested, int chunks, int calls, long duration, string reason, CancellationToken ct)
    {
        var entry = new AIGenerationLog
        {
            MaterialId = materialId, Status = "Failed", ErrorMessage = reason,
            RequestedQuestionCount = requested, GeneratedQuestionCount = 0, ChunkCount = chunks,
            ProviderRequestCount = calls, DurationMilliseconds = duration
        };
        try { _db.AIGenerationLogs.Add(entry); await _db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _db.Entry(entry).State = EntityState.Detached;
            _log?.LogWarning("AI generation failure could not be recorded");
        }
    }
}
