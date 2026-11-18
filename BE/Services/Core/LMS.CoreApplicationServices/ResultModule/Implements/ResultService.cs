using System.Globalization;
using LMS.Constants;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.ResultModule.Abstracts;
using LMS.CoreApplicationServices.ResultModule.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.ResultModule.Implements;
public sealed class ResultService : IResultService
{
    private readonly LmsDbContext _db;
    public ResultService(LmsDbContext db) => _db = db;
    public async Task<AttemptResultDto> GetAsync(int attemptId, int userId, string role, CancellationToken ct)
    {
        var attempt = await _db.QuizAttempts.AsNoTracking().Include(a => a.Quiz)
            .Include(a => a.StudentAnswers).ThenInclude(a => a.SelectedOption)
            .Include(a => a.StudentAnswers).ThenInclude(a => a.Question).ThenInclude(q => q.Topic)
            .Include(a => a.StudentAnswers).ThenInclude(a => a.Question).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(a => a.Id == attemptId && a.Status == AttemptStatus.Submitted, ct) ?? throw new AppException(404, "Không tìm thấy Attempt");
        var ownsResult = role == Roles.Student && attempt.StudentId == userId;
        var teachesCourse = role == Roles.Lecturer && await _db.Courses.AnyAsync(c => c.Id == attempt.Quiz.CourseId && c.LecturerId == userId, ct);
        if (!ownsResult && !teachesCourse) throw new AppException(403, "Không có quyền xem kết quả");
        var accuracies = attempt.StudentAnswers.GroupBy(a => new { a.Question.TopicId, a.Question.Topic.Name })
            .Select(g => new TopicAccuracyDto(g.Key.TopicId, g.Key.Name,
                Math.Round(100.0 * g.Count(a => a.IsCorrect) / g.Count(), 1),
                RecommendationPolicy.IsWeak(g.Count(a => a.IsCorrect), g.Count()))).OrderBy(t => t.TopicId).ToList();
        var currentMember = await _db.Enrollments.AnyAsync(e => e.CourseId == attempt.Quiz.CourseId && e.StudentId == attempt.StudentId, ct);
        var rows = currentMember
            ? await _db.MaterialRecommendations.AsNoTracking().Where(r => r.AttemptId == attemptId && !r.Material.IsDeleted && r.Material.CourseId == attempt.Quiz.CourseId &&
                r.Material.TopicId == r.TopicId && r.Material.Topic.CourseId == attempt.Quiz.CourseId)
                .Select(r => new { r.TopicId, r.MaterialId, r.Material.Title }).Distinct().OrderBy(r => r.MaterialId).ToListAsync(ct)
            : [];
        var recommendations = new List<RecommendationDto>();
        foreach (var topic in accuracies.Where(t => t.IsWeak))
        {
            var topicAnswers = attempt.StudentAnswers.Where(a => a.Question.TopicId == topic.TopicId).ToList();
            var reason = WeakReason(topicAnswers.Count(a => a.IsCorrect), topicAnswers.Count);
            var materials = rows.Where(m => m.TopicId == topic.TopicId).Take(RecommendationPolicy.MaxMaterialsPerTopic).ToList();
            if (materials.Count == 0)
                recommendations.Add(new(topic.TopicId, topic.TopicName, null, null, topic.Accuracy, attempt.Quiz.CourseId, null, "RuleBased", reason));
            else
                recommendations.AddRange(materials.Select(m => new RecommendationDto(topic.TopicId, topic.TopicName, m.MaterialId,
                    m.Title, topic.Accuracy, attempt.Quiz.CourseId, $"/api/materials/{m.MaterialId}/download", "RuleBased", reason)));
        }
        var answers = attempt.StudentAnswers.OrderBy(a => a.QuestionId).Select(a => new AnswerFeedbackDto(a.QuestionId,
            a.Question.Content, a.SelectedOption.Content, a.Question.Options.Single(o => o.IsCorrect).Content,
            a.IsCorrect, a.Question.Explanation, a.Question.Topic.Name)).ToList();
        var correct = attempt.StudentAnswers.Count(a => a.IsCorrect);
        var total = attempt.StudentAnswers.Count;
        return new(attempt.Id, attempt.Score, correct, total, accuracies, recommendations, answers, total - correct,
            total == 0 ? 0 : Math.Round(100.0 * correct / total, 1));
    }
    public async Task<List<RecommendationDto>> MyRecommendationsAsync(int studentId, CancellationToken ct)
        => await RecommendationsAsync(studentId, await TopicTotalsAsync(studentId, ct), ct);

    private IQueryable<QuizAttempt> CurrentAttempts(int studentId)
        => _db.QuizAttempts.AsNoTracking().Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Submitted &&
            _db.Enrollments.Any(e => e.StudentId == studentId && e.CourseId == a.Quiz.CourseId));

    private async Task<List<TopicTotal>> TopicTotalsAsync(int studentId, CancellationToken ct)
    {
        // Aggregate the authenticated student's real answers, including only currently enrolled courses.
        return await _db.StudentAnswers.AsNoTracking().Where(a => a.Attempt.StudentId == studentId && a.Attempt.Status == AttemptStatus.Submitted &&
                a.Question.CourseId == a.Attempt.Quiz.CourseId && a.Question.Topic.CourseId == a.Question.CourseId &&
                _db.Enrollments.Any(e => e.StudentId == studentId && e.CourseId == a.Attempt.Quiz.CourseId))
            .GroupBy(a => new { a.Question.TopicId, a.Question.Topic.Name, a.Question.CourseId, CourseTitle = a.Question.Course.Title })
            .Select(g => new TopicTotal(g.Key.TopicId, g.Key.Name, g.Key.CourseId, g.Key.CourseTitle, g.Count(a => a.IsCorrect), g.Count()))
            .ToListAsync(ct);
    }

    private async Task<List<RecommendationDto>> RecommendationsAsync(int studentId, List<TopicTotal> totals, CancellationToken ct)
    {
        var weak = totals.Where(t => RecommendationPolicy.IsWeak(t.Correct, t.Total)).OrderBy(t => t.CourseId).ThenBy(t => t.TopicId).ToList();
        if (weak.Count == 0) return [];
        var topicIds = weak.Select(t => t.TopicId).ToList();
        var materials = await _db.LearningMaterials.AsNoTracking().Where(m => !m.IsDeleted && topicIds.Contains(m.TopicId) &&
                m.Topic.CourseId == m.CourseId && _db.Enrollments.Any(e => e.StudentId == studentId && e.CourseId == m.CourseId))
            .OrderBy(m => m.Id).Select(m => new { m.Id, m.TopicId, m.CourseId, m.Title }).ToListAsync(ct);
        var result = new List<RecommendationDto>();
        foreach (var topic in weak)
        {
            var accuracy = Math.Round(100.0 * topic.Correct / topic.Total, 1);
            var reason = WeakReason(topic.Correct, topic.Total);
            var candidates = materials.Where(m => m.TopicId == topic.TopicId && m.CourseId == topic.CourseId).Take(RecommendationPolicy.MaxMaterialsPerTopic).ToList();
            if (candidates.Count == 0) result.Add(new(topic.TopicId, topic.Name, null, null, accuracy, topic.CourseId, null, "RuleBased", reason));
            else result.AddRange(candidates.Select(m => new RecommendationDto(topic.TopicId, topic.Name, m.Id, m.Title,
                accuracy, topic.CourseId, $"/api/materials/{m.Id}/download", "RuleBased", reason)));
        }
        return result;
    }
    public async Task<List<AttemptSummaryDto>> MyAttemptsAsync(int studentId, CancellationToken ct)
        => await _db.QuizAttempts.AsNoTracking().Where(a => a.StudentId == studentId && a.Status == AttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id)
            .Select(a => new AttemptSummaryDto(a.Id, a.QuizId, a.Quiz.Title, a.Score, a.SubmittedAt)).ToListAsync(ct);
    public async Task<List<CourseAttemptDto>> CourseAttemptsAsync(int courseId, int lecturerId, CancellationToken ct)
    {
        var ownerId = await _db.Courses.Where(c => c.Id == courseId).Select(c => (int?)c.LecturerId).SingleOrDefaultAsync(ct)
            ?? throw new AppException(404, "Không tìm thấy Course");
        if (ownerId != lecturerId) throw new AppException(403, "Không có quyền xem bài nộp của khóa học");
        return await _db.QuizAttempts.AsNoTracking().Where(a => a.Quiz.CourseId == courseId && a.Status == AttemptStatus.Submitted)
            .OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id)
            .Select(a => new CourseAttemptDto(a.Id, a.QuizId, a.Quiz.Title, a.StudentId, a.Student.FullName, a.Score, a.SubmittedAt)).ToListAsync(ct);
    }

    public async Task<StudentDashboardDto> DashboardAsync(int studentId, CancellationToken ct)
    {
        var courses = await _db.Enrollments.AsNoTracking().Where(e => e.StudentId == studentId)
            .OrderBy(e => e.CourseId).Select(e => new
            {
                e.CourseId, e.Course.Code, e.Course.Title,
                Published = _db.Quizzes.Count(q => q.CourseId == e.CourseId && q.Status == QuizStatus.Published)
            }).ToListAsync(ct);
        if (courses.Count == 0) return new(0, 0, 0, null, 0, [], [], [], []);
        var attempts = CurrentAttempts(studentId);
        var metrics = await attempts.GroupBy(a => a.Quiz.CourseId).Select(g => new
        {
            CourseId = g.Key, Average = g.Average(a => (decimal?)a.Score),
            Completed = g.Where(a => a.Quiz.Status == QuizStatus.Published).Select(a => a.QuizId).Distinct().Count()
        }).ToListAsync(ct);
        var progress = courses.Select(course =>
        {
            var metric = metrics.SingleOrDefault(m => m.CourseId == course.CourseId);
            return new StudentCourseProgressDto(course.CourseId, course.Code, course.Title, course.Published,
                metric?.Completed ?? 0, Percent(metric?.Completed ?? 0, course.Published), RoundScore(metric?.Average));
        }).ToList();
        var average = await attempts.Select(a => (decimal?)a.Score).AverageAsync(ct);
        var recent = await attempts.OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id).Take(10)
            .Select(a => new AttemptSummaryDto(a.Id, a.QuizId, a.Quiz.Title, a.Score, a.SubmittedAt)).ToListAsync(ct);
        var topicTotals = await TopicTotalsAsync(studentId, ct);
        var weak = topicTotals.Where(t => RecommendationPolicy.IsWeak(t.Correct, t.Total)).OrderBy(t => t.CourseId).ThenBy(t => t.TopicId)
            .Select(t => new StudentWeakTopicDto(t.CourseId, t.CourseTitle, t.TopicId, t.Name, Percent(t.Correct, t.Total),
                t.Correct, t.Total, WeakReason(t.Correct, t.Total))).ToList();
        var completed = progress.Sum(p => p.CompletedQuizCount);
        var published = progress.Sum(p => p.PublishedQuizCount);
        return new(courses.Count, completed, published, RoundScore(average), Percent(completed, published), progress,
            recent, weak, await RecommendationsAsync(studentId, topicTotals, ct));
    }

    public async Task<CourseStatisticsDto> CourseStatisticsAsync(int courseId, int lecturerId, CancellationToken ct)
    {
        var owner = await _db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => (int?)c.LecturerId).SingleOrDefaultAsync(ct)
            ?? throw new AppException(404, "Không tìm thấy Course");
        if (owner != lecturerId) throw new AppException(403, "Không có quyền xem thống kê của khóa học");
        var attempts = _db.QuizAttempts.AsNoTracking().Where(a => a.Quiz.CourseId == courseId && a.Status == AttemptStatus.Submitted &&
            _db.Enrollments.Any(e => e.StudentId == a.StudentId && e.CourseId == courseId));
        var aggregate = await attempts.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Average = g.Average(a => (decimal?)a.Score) })
            .SingleOrDefaultAsync(ct);
        var quizzes = await _db.Quizzes.AsNoTracking().Where(q => q.CourseId == courseId && q.Status == QuizStatus.Published)
            .OrderBy(q => q.Id).Select(q => new { q.Id, q.Title, q.PublishedAt }).ToListAsync(ct);
        var quizMetrics = await attempts.GroupBy(a => a.QuizId).Select(g => new
        { QuizId = g.Key, Students = g.Select(a => a.StudentId).Distinct().Count(), Average = g.Average(a => (decimal?)a.Score) }).ToListAsync(ct);
        var quizStats = quizzes.Select(quiz =>
        {
            var metric = quizMetrics.SingleOrDefault(m => m.QuizId == quiz.Id);
            return new QuizStatisticsDto(quiz.Id, quiz.Title, quiz.PublishedAt, metric?.Students ?? 0, RoundScore(metric?.Average));
        }).ToList();
        var students = await _db.Enrollments.AsNoTracking().Where(e => e.CourseId == courseId).OrderBy(e => e.StudentId)
            .Select(e => new { e.StudentId, e.Student.FullName }).ToListAsync(ct);
        var studentMetrics = await attempts.GroupBy(a => a.StudentId).Select(g => new
        {
            StudentId = g.Key, Average = g.Average(a => (decimal?)a.Score),
            Completed = g.Where(a => a.Quiz.Status == QuizStatus.Published).Select(a => a.QuizId).Distinct().Count()
        }).ToListAsync(ct);
        var studentStats = students.Select(student =>
        {
            var metric = studentMetrics.SingleOrDefault(m => m.StudentId == student.StudentId);
            return new StudentStatisticsDto(student.StudentId, student.FullName, metric?.Completed ?? 0, quizzes.Count,
                Percent(metric?.Completed ?? 0, quizzes.Count), RoundScore(metric?.Average));
        }).ToList();
        var topicMetrics = await _db.StudentAnswers.AsNoTracking().Where(a => a.Attempt.Quiz.CourseId == courseId &&
                a.Attempt.Status == AttemptStatus.Submitted && a.Question.CourseId == courseId && a.Question.Topic.CourseId == courseId &&
                _db.Enrollments.Any(e => e.CourseId == courseId && e.StudentId == a.Attempt.StudentId))
            .GroupBy(a => new { a.Question.TopicId, a.Question.Topic.Name }).Select(g => new
            {
                g.Key.TopicId, g.Key.Name, Correct = g.Count(a => a.IsCorrect), Total = g.Count(),
                Affected = g.Where(a => !a.IsCorrect).Select(a => a.Attempt.StudentId).Distinct().Count()
            }).ToListAsync(ct);
        var weak = topicMetrics.Where(t => RecommendationPolicy.IsWeak(t.Correct, t.Total))
            .OrderBy(t => (double)t.Correct / t.Total).ThenBy(t => t.TopicId)
            .Select(t => new CourseWeakTopicDto(t.TopicId, t.Name, Percent(t.Correct, t.Total), t.Total - t.Correct,
                t.Total, t.Affected, WeakReason(t.Correct, t.Total))).ToList();
        return new(courseId, students.Count, quizzes.Count, aggregate?.Count ?? 0, RoundScore(aggregate?.Average), quizStats, studentStats, weak);
    }

    private static double Percent(int count, int total) => total == 0 ? 0 : Math.Round(100.0 * count / total, 1);
    private static decimal? RoundScore(decimal? score) => score.HasValue ? Math.Round(score.Value, 2, MidpointRounding.AwayFromZero) : null;
    private static string WeakReason(int correct, int total)
        => $"Đúng {correct}/{total} câu ({Percent(correct, total).ToString("0.#", CultureInfo.InvariantCulture)}%), dưới ngưỡng {RecommendationPolicy.ThresholdText}; nên ôn lại chủ đề theo quy tắc kết quả Quiz.";
    private sealed record TopicTotal(int TopicId, string Name, int CourseId, string CourseTitle, int Correct, int Total);
}
