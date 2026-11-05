using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.QuestionModule.Dtos;
using LMS.CoreApplicationServices.QuestionModule.Implements;
using LMS.CoreApplicationServices.QuizModule.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.QuizModule.Implements;
public sealed class QuizService
{
    private readonly LmsDbContext _db;
    private readonly ICourseService _courses;
    public QuizService(LmsDbContext db, ICourseService courses) { _db = db; _courses = courses; }
    private static QuizDto Map(Quiz x) => new(x.Id, x.CourseId, x.Title, x.Status, x.PublishedAt, x.Description, x.QuizQuestions.Count);
    private static void ValidateDetails(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 150 || (description?.Length ?? 0) > 2000)
            throw new AppException(400, "Tên Quiz cần từ 1 đến 150 ký tự; mô tả tối đa 2000 ký tự");
    }
    private async Task<List<int>> ValidateQuestionIdsAsync(int courseId, List<int>? questionIds, bool requireAny, CancellationToken ct)
    {
        if (questionIds is null || questionIds.Count > 200 || (requireAny && questionIds.Count == 0) ||
            questionIds.Any(id => id <= 0) || questionIds.Distinct().Count() != questionIds.Count)
            throw new AppException(400, "Chọn tối đa 200 câu hỏi không trùng; Quiz phát hành cần ít nhất 1 câu");
        if (questionIds.Count == 0) return questionIds;
        var questions = await _db.Questions.AsNoTracking().Include(q => q.Options).Where(q => questionIds.Contains(q.Id)).ToListAsync(ct);
        if (questions.Count != questionIds.Count || questions.Any(q => q.CourseId != courseId || q.Status != "Approved"))
            throw new AppException(400, "Chỉ chọn câu hỏi Approved thuộc cùng Course");
        foreach (var q in questions)
            QuestionService.ValidateOptions(q.Content, q.Explanation, q.Options.Select(o => new OptionInput(o.Content, o.IsCorrect)).ToList());
        return questionIds;
    }
    public async Task<List<QuizDto>> ListAsync(int courseId, int userId, string role, CancellationToken ct)
    {
        await _courses.RequireMemberAsync(courseId, userId, role, ct);
        var query = _db.Quizzes.AsNoTracking().Where(x => x.CourseId == courseId);
        if (role == "Student") query = query.Where(x => x.Status == "Published");
        return await query.OrderByDescending(x => x.Id).Select(x => new QuizDto(x.Id, x.CourseId, x.Title,
            x.Status, x.PublishedAt, x.Description, _db.QuizQuestions.Count(qq => qq.QuizId == x.Id))).ToListAsync(ct);
    }
    public async Task<QuizAuthoringDto> AuthoringAsync(int quizId, int lecturerId, CancellationToken ct)
    {
        var quiz = await _db.Quizzes.AsNoTracking().Include(q => q.QuizQuestions).ThenInclude(qq => qq.Question).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(q => q.Id == quizId, ct) ?? throw new AppException(404, "Không thấy Quiz");
        await _courses.RequireOwnerAsync(quiz.CourseId, lecturerId, ct);
        var selected = quiz.QuizQuestions.OrderBy(q => q.QuestionId).ToList();
        return new(quiz.Id, quiz.CourseId, quiz.Title, quiz.Description, quiz.Status, quiz.PublishedAt,
            selected.Select(q => q.QuestionId).ToList(), selected.Select(q => QuestionService.Map(q.Question)).ToList());
    }
    public async Task<QuizDto> CreateAsync(int lecturerId, QuizCreateRequest request, CancellationToken ct)
    {
        await _courses.RequireOwnerAsync(request.CourseId, lecturerId, ct);
        ValidateDetails(request.Title, request.Description);
        var ids = await ValidateQuestionIdsAsync(request.CourseId, request.QuestionIds ?? new(), false, ct);
        var quiz = new Quiz { CourseId = request.CourseId, Title = request.Title.Trim(), Description = request.Description?.Trim() ?? "",
            QuizQuestions = ids.Select(id => new QuizQuestion { QuestionId = id }).ToList() };
        _db.Quizzes.Add(quiz);
        await _db.SaveChangesAsync(ct);
        return Map(quiz);
    }
    public async Task<QuizDto> UpdateAsync(int quizId, int lecturerId, QuizUpdateRequest request, CancellationToken ct)
    {
        var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).SingleOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new AppException(404, "Không thấy Quiz");
        await _courses.RequireOwnerAsync(quiz.CourseId, lecturerId, ct);
        if (quiz.Status != "Draft") throw new AppException(409, "Chỉ sửa Quiz Draft");
        ValidateDetails(request.Title, request.Description);
        var ids = await ValidateQuestionIdsAsync(quiz.CourseId, request.QuestionIds ?? quiz.QuizQuestions.Select(q => q.QuestionId).ToList(), false, ct);
        quiz.Title = request.Title.Trim();
        quiz.Description = request.Description?.Trim() ?? "";
        ReplaceQuestions(quiz, ids);
        quiz.Revision++;
        // A selection-only edit otherwise emits no parent UPDATE and skips its concurrency token.
        _db.Entry(quiz).Property(x => x.Status).IsModified = true;
        await _db.SaveChangesAsync(ct);
        return Map(quiz);
    }
    private void ReplaceQuestions(Quiz quiz, List<int> ids)
    {
        _db.QuizQuestions.RemoveRange(quiz.QuizQuestions.Where(q => !ids.Contains(q.QuestionId)));
        var existingIds = quiz.QuizQuestions.Select(q => q.QuestionId).ToHashSet();
        foreach (var id in ids.Where(id => !existingIds.Contains(id)))
            quiz.QuizQuestions.Add(new QuizQuestion { QuizId = quiz.Id, QuestionId = id });
    }
    public async Task<QuizDto> PublishAsync(int quizId, int lecturerId, QuizPublishRequest request, CancellationToken ct)
    {
        var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).SingleOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new AppException(404, "Không thấy Quiz");
        await _courses.RequireOwnerAsync(quiz.CourseId, lecturerId, ct);
        if (quiz.Status != "Draft") throw new AppException(409, "Quiz đã được publish");
        var ids = await ValidateQuestionIdsAsync(quiz.CourseId, request.QuestionIds, true, ct);
        ReplaceQuestions(quiz, ids);
        quiz.Status = "Published";
        quiz.Revision++;
        quiz.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Map(quiz);
    }
    private async Task<Quiz> PublishedQuizAsync(int quizId, int studentId, CancellationToken ct)
    {
        var quiz = await _db.Quizzes.AsNoTracking().Include(q => q.QuizQuestions).ThenInclude(qq => qq.Question).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(q => q.Id == quizId, ct);
        if (quiz is null || quiz.Status != "Published") throw new AppException(404, "Quiz không khả dụng");
        await _courses.RequireMemberAsync(quiz.CourseId, studentId, "Student", ct);
        if (quiz.QuizQuestions.Count == 0) throw new AppException(409, "Quiz chưa có câu hỏi hợp lệ");
        return quiz;
    }
    public async Task<QuizTakeDto> TakeAsync(int quizId, int studentId, CancellationToken ct)
    {
        var quiz = await PublishedQuizAsync(quizId, studentId, ct);
        // IsCorrect and Explanation are intentionally absent from this contract.
        return new(quiz.Id, quiz.Title, quiz.QuizQuestions.OrderBy(qq => qq.QuestionId)
            .Select(qq => new TakeQuestionDto(qq.QuestionId, qq.Question.Content,
                qq.Question.Options.OrderBy(o => o.Id).Select(o => new TakeOptionDto(o.Id, o.Content)).ToList())).ToList(), quiz.Description);
    }
    public async Task<SubmitResponse> SubmitAsync(int quizId, int studentId, SubmitRequest request, CancellationToken ct)
    {
        var quiz = await PublishedQuizAsync(quizId, studentId, ct);
        if (await _db.QuizAttempts.AnyAsync(a => a.QuizId == quizId && a.StudentId == studentId, ct))
            throw new AppException(409, "Sinh viên đã nộp Quiz này");
        var questions = quiz.QuizQuestions.ToDictionary(q => q.QuestionId, q => q.Question);
        if (request.Answers is null || request.Answers.Count != questions.Count || request.Answers.Any(a => a is null) ||
            !request.Answers.Select(a => a.QuestionId).Order().SequenceEqual(questions.Keys.Order()))
            throw new AppException(400, "Phải trả lời mỗi câu hỏi đúng một lần");
        var mapped = new List<StudentAnswer>();
        foreach (var answer in request.Answers)
        {
            var question = questions[answer.QuestionId];
            var option = question.Options.SingleOrDefault(o => o.Id == answer.SelectedOptionId)
                ?? throw new AppException(400, "Lựa chọn không thuộc Question");
            if (question.Options.Count != 4 || question.Options.Count(o => o.IsCorrect) != 1)
                throw new AppException(409, "Quiz có câu hỏi không hợp lệ; liên hệ giảng viên");
            mapped.Add(new StudentAnswer { QuestionId = question.Id, SelectedOptionId = option.Id, IsCorrect = option.IsCorrect });
        }
        var correct = mapped.Count(a => a.IsCorrect);
        var score = Math.Round((decimal)correct * 10m / mapped.Count, 2, MidpointRounding.AwayFromZero);
        var attempt = new QuizAttempt { QuizId = quizId, StudentId = studentId, Score = score, StudentAnswers = mapped };
        var weakIds = mapped.GroupBy(a => questions[a.QuestionId].TopicId)
            .Where(g => (double)g.Count(a => a.IsCorrect) / g.Count() < 0.60).Select(g => g.Key).ToList();
        var candidates = await _db.LearningMaterials.AsNoTracking()
            .Where(m => !m.IsDeleted && m.CourseId == quiz.CourseId && weakIds.Contains(m.TopicId)).OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.TopicId }).ToListAsync(ct);
        var recommendations = candidates.GroupBy(m => m.TopicId).SelectMany(g => g.Take(2))
            .Select(m => new MaterialRecommendation { Attempt = attempt, MaterialId = m.Id, TopicId = m.TopicId }).ToList();
        // EF commits the entire graph in one transaction. The unique (QuizId, StudentId) index is the final race guard.
        _db.QuizAttempts.Add(attempt);
        _db.MaterialRecommendations.AddRange(recommendations);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            foreach (var entity in recommendations.Cast<object>().Concat(mapped).Append(attempt).ToList())
                _db.Entry(entity).State = EntityState.Detached;
            throw new AppException(409, "Sinh viên đã nộp Quiz này");
        }
        return new(attempt.Id, score, correct, mapped.Count);
    }
}
