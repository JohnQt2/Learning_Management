using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.QuestionModule.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.QuestionModule.Implements;
public sealed class QuestionService
{
    private readonly LmsDbContext _db;
    private readonly ICourseService _courses;
    public QuestionService(LmsDbContext db, ICourseService courses) { _db = db; _courses = courses; }
    public static QuestionDto Map(Question q) => new(q.Id, q.CourseId, q.TopicId, q.MaterialId, q.Content, q.Explanation, q.Status, q.Source, q.Options.OrderBy(o => o.Id).Select(o => new OptionDisplay(o.Id, o.Content, o.IsCorrect)).ToList(),
        q.Difficulty, q.SourceChunkIndex, q.SourceStartCharacter, q.SourceEndCharacter);
    private static string? ValidateDifficulty(string? difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty)) return null;
        if (difficulty is not ("Easy" or "Medium" or "Hard")) throw new AppException(400, "Độ khó phải là Easy, Medium hoặc Hard");
        return difficulty;
    }
    public async Task<List<QuestionDto>> ListAsync(int courseId, int lecturerId, CancellationToken ct)
    {
        await _courses.RequireOwnerAsync(courseId, lecturerId, ct);
        return (await _db.Questions.AsNoTracking().Include(x => x.Options).Where(x => x.CourseId == courseId).OrderByDescending(x => x.Id).ToListAsync(ct)).Select(Map).ToList();
    }
    public static void ValidateOptions(string content, string? explanation, List<OptionInput>? options)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 4000 ||
            (explanation?.Length ?? 0) > 4000 || options is not { Count: 4 } || options.Any(o => o is null) ||
            options.Count(o => o.IsCorrect) != 1 || options.Any(o => string.IsNullOrWhiteSpace(o.Content) || o.Content.Trim().Length > 2000) ||
            options.Select(o => o.Content.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new AppException(400, "Cần nội dung, 4 lựa chọn không rỗng và đúng một đáp án");
    }
    public async Task<QuestionDto> CreateAsync(int lecturerId, QuestionCreateRequest request, CancellationToken ct)
    {
        await _courses.RequireOwnerAsync(request.CourseId, lecturerId, ct);
        if (!await _db.Topics.AnyAsync(t => t.Id == request.TopicId && t.CourseId == request.CourseId, ct))
            throw new AppException(400, "Topic không thuộc Course");
        ValidateOptions(request.Content, request.Explanation, request.Options);
        var q = new Question
        {
            CourseId = request.CourseId, TopicId = request.TopicId,
            Content = request.Content.Trim(), Explanation = request.Explanation?.Trim() ?? "",
            Source = "Manual", Status = "Draft", Difficulty = ValidateDifficulty(request.Difficulty),
            Options = request.Options.Select(o => new AnswerOption { Content = o.Content.Trim(), IsCorrect = o.IsCorrect }).ToList()
        };
        _db.Questions.Add(q);
        await _db.SaveChangesAsync(ct);
        return Map(q);
    }
    public async Task<QuestionDto> EditAsync(int id, int lecturerId, QuestionEditRequest request, CancellationToken ct)
    {
        var q = await _db.Questions.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException(404, "Không tìm thấy câu hỏi");
        await _courses.RequireOwnerAsync(q.CourseId, lecturerId, ct);
        if (q.Status != "Draft") throw new AppException(409, "Chỉ chỉnh sửa Question Draft");
        if (await _db.QuizQuestions.AnyAsync(qq => qq.QuestionId == id, ct) || await _db.StudentAnswers.AnyAsync(a => a.QuestionId == id, ct))
            throw new AppException(409, "Không sửa câu hỏi đã dùng trong Quiz; hãy tạo câu hỏi mới");
        ValidateOptions(request.Content, request.Explanation, request.Options);
        q.Content = request.Content.Trim(); q.Explanation = request.Explanation?.Trim() ?? "";
        if (request.Difficulty is not null) q.Difficulty = ValidateDifficulty(request.Difficulty);
        _db.AnswerOptions.RemoveRange(q.Options);
        q.Options = request.Options.Select(o => new AnswerOption { Content = o.Content.Trim(), IsCorrect = o.IsCorrect }).ToList();
        // Child-only changes must still check the parent's original Draft status atomically.
        _db.Entry(q).Property(x => x.Status).IsModified = true;
        await _db.SaveChangesAsync(ct);
        return Map(q);
    }
    public async Task<QuestionDto> ApproveAsync(int id, int lecturerId, CancellationToken ct)
    {
        var q = await _db.Questions.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException(404, "Không tìm thấy câu hỏi");
        await _courses.RequireOwnerAsync(q.CourseId, lecturerId, ct);
        if (q.Status != "Draft") throw new AppException(409, "Question đã duyệt hoặc không còn Draft");
        ValidateOptions(q.Content, q.Explanation, q.Options.Select(o => new OptionInput(o.Content, o.IsCorrect)).ToList());
        q.Status = "Approved"; await _db.SaveChangesAsync(ct);
        return Map(q);
    }
}
