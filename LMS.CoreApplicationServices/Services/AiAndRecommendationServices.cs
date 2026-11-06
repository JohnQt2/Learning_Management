using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Data;
using LMS.Core.Infrastructure.Entities;
using LMS.CoreApplicationServices.DTOs;
using LMS.CoreApplicationServices.Validators;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.Services
{
    // --- UC05: AI sinh câu hỏi ở trạng thái Draft ---
    public class AIQuestionService
    {
        private readonly LmsDbContext _db;
        private readonly IAIClient _aiClient;
        private readonly QuestionJsonValidator _validator;

        public AIQuestionService(LmsDbContext db, IAIClient aiClient, QuestionJsonValidator validator)
        {
            _db = db;
            _aiClient = aiClient;
            _validator = validator;
        }

        public async Task<List<QuestionDto>> GenerateAsync(GenerateAIQuestionsDto dto, int lecturerId)
        {
            var material = await _db.LearningMaterials
                .Include(m => m.Course)
                .FirstOrDefaultAsync(m => m.Id == dto.MaterialId);

            if (material == null)
                throw new AppException(404, "Không tìm thấy học liệu");
            if (material.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course"); // UC05 alt branch
            if (string.IsNullOrWhiteSpace(material.ExtractedText))
                throw new AppException(400, "ExtractedText rỗng, không thể gọi AI"); // Tiết kiệm chi phí

            string prompt = $"Sinh {dto.Count} câu hỏi trắc nghiệm độ khó {dto.Difficulty} từ văn bản sau theo đúng JSON schema: {material.ExtractedText}";

            string rawJson;
            try
            {
                rawJson = await _aiClient.GenerateQuestionsAsync(prompt, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _db.AIGenerationLogs.Add(new AIGenerationLog { MaterialId = material.Id, Status = "Failed", ErrorMessage = ex.Message });
                await _db.SaveChangesAsync();
                throw new AppException(422, "AI sinh câu hỏi thất bại (Timeout/Lỗi dịch vụ) - thử lại hoặc soạn thủ công"); // UC05
            }

            var validation = _validator.ParseAndValidate(rawJson, dto.Count);
            if (!validation.IsValid)
            {
                // Từ chối toàn bộ lô (reject batch), ghi log lỗi, không lưu dữ liệu bẩn
                _db.AIGenerationLogs.Add(new AIGenerationLog { MaterialId = material.Id, Status = "Failed", ErrorMessage = validation.ErrorMessage });
                await _db.SaveChangesAsync();
                throw new AppException(422, $"AI sinh câu hỏi thất bại ({validation.ErrorMessage}) - thử lại hoặc soạn câu hỏi thủ công");
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var createdQuestions = new List<Question>();
                foreach (var item in validation.Questions)
                {
                    var q = new Question
                    {
                        CourseId = material.CourseId,
                        TopicId = material.TopicId,
                        MaterialId = material.Id,
                        Content = item.Content,
                        Explanation = item.Explanation,
                        Source = "AI",
                        Status = "Draft", // Luôn lưu ở trạng thái Draft
                        Options = item.Options.Select(o => new AnswerOption
                        {
                            Content = o.Content,
                            IsCorrect = o.IsCorrect
                        }).ToList()
                    };
                    _db.Questions.Add(q);
                    createdQuestions.Add(q);
                }

                _db.AIGenerationLogs.Add(new AIGenerationLog { MaterialId = material.Id, Status = "Success" });
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return createdQuestions.Select(q => new QuestionDto(
                    q.Id, q.Content, q.Explanation, q.Status, q.Source,
                    q.Options.Select(o => new OptionDto(o.Id, o.Content, o.IsCorrect)).ToList()
                )).ToList();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }

    // --- UC09: Xem kết quả, Phân tích Topic yếu (< 60%) & Gợi ý tài liệu ôn tập ---
    public class ResultService
    {
        private readonly LmsDbContext _db;
        public ResultService(LmsDbContext db) => _db = db;

        public async Task<AttemptResultDto> GetResultAsync(int attemptId, int currentUserId)
        {
            var attempt = await _db.QuizAttempts
                .Include(a => a.Quiz).ThenInclude(q => q.Course)
                .Include(a => a.StudentAnswers).ThenInclude(sa => sa.Question).ThenInclude(q => q.Topic)
                .FirstOrDefaultAsync(a => a.Id == attemptId)
                ?? throw new AppException(404, "Không tìm thấy bài làm");

            // Chỉ chủ bài làm hoặc Giảng viên của Course mới được xem
            if (attempt.StudentId != currentUserId && attempt.Quiz.Course.LecturerId != currentUserId)
                throw new AppException(403, "Bạn không có quyền xem kết quả bài làm này");

            // 1. Tính accuracy theo từng Topic xuất hiện trong bài làm
            var topicAccuracies = attempt.StudentAnswers
                .GroupBy(sa => new { sa.Question.TopicId, TopicName = sa.Question.Topic.Name })
                .Select(g =>
                {
                    double acc = Math.Round((double)g.Count(x => x.IsCorrect) / g.Count() * 100.0, 1);
                    return new TopicAccuracyDto(g.Key.TopicId, g.Key.TopicName, acc, acc < 60.0);
                })
                .ToList();

            // 2. Chọn các Topic yếu có accuracy < 60%
            var weakTopicIds = topicAccuracies.Where(t => t.IsWeak).Select(t => t.TopicId).ToList();
            var recommendations = new List<RecommendationDto>();

            if (weakTopicIds.Any())
            {
                // 3. Lọc LearningMaterial cùng Course và thuộc Topic yếu (tối đa 2 Material / Topic)
                var candidateMaterials = await _db.LearningMaterials
                    .Include(m => m.Topic)
                    .Where(m => m.CourseId == attempt.Quiz.CourseId && weakTopicIds.Contains(m.TopicId))
                    .ToListAsync();

                var selectedMaterials = candidateMaterials
                    .GroupBy(m => m.TopicId)
                    .SelectMany(g => g.Take(2))
                    .ToList();

                foreach (var mat in selectedMaterials)
                {
                    recommendations.Add(new RecommendationDto(mat.TopicId, mat.Topic.Name, mat.Id, mat.Title));

                    bool logged = await _db.MaterialRecommendations
                        .AnyAsync(r => r.AttemptId == attempt.Id && r.MaterialId == mat.Id);
                    if (!logged)
                    {
                        _db.MaterialRecommendations.Add(new MaterialRecommendation
                        {
                            AttemptId = attempt.Id,
                            TopicId = mat.TopicId,
                            MaterialId = mat.Id
                        });
                    }
                }
                await _db.SaveChangesAsync();
            }

            int correctCount = attempt.StudentAnswers.Count(sa => sa.IsCorrect);
            return new AttemptResultDto(
                attempt.Id,
                attempt.Score,
                correctCount,
                attempt.StudentAnswers.Count,
                topicAccuracies,
                recommendations
            );
        }
    }
}
