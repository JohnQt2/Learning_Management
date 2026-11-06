using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Data;
using LMS.Core.Infrastructure.Entities;
using LMS.CoreApplicationServices.DTOs;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.Services
{
    // --- UC04: Upload tài liệu & Trích xuất văn bản ---
    public class MaterialService
    {
        private readonly LmsDbContext _db;
        private readonly IFileStorage _storage;
        private readonly ITextExtractor _extractor;

        public MaterialService(LmsDbContext db, IFileStorage storage, ITextExtractor extractor)
        {
            _db = db;
            _storage = storage;
            _extractor = extractor;
        }

        public async Task<MaterialDto> UploadMaterialAsync(int courseId, UploadMaterialDto dto, int lecturerId)
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
            if (course == null) throw new AppException(404, "Không tìm thấy khóa học");
            if (course.LecturerId != lecturerId) throw new AppException(403, "Không có quyền trên khóa học này"); // Kiểm tra owner

            var ext = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
            if ((ext != ".pdf" && ext != ".txt") || dto.File.Length == 0 || dto.File.Length > 10 * 1024 * 1024)
                throw new AppException(400, "Chỉ chấp nhận PDF/TXT dưới giới hạn cho phép"); // UC04 alt branch

            using var stream = dto.File.OpenReadStream();
            var storagePath = await _storage.SaveAsync(stream, dto.File.FileName);
            var extractedText = await _extractor.ExtractTextAsync(storagePath, dto.File.ContentType);

            var material = new LearningMaterial
            {
                CourseId = courseId,
                TopicId = dto.TopicId,
                Title = dto.Title,
                FilePath = storagePath,
                ExtractedText = extractedText ?? string.Empty
            };

            _db.LearningMaterials.Add(material);
            await _db.SaveChangesAsync();

            // Nếu trích xuất rỗng, tài liệu vẫn lưu nhưng khóa chức năng gọi AI sinh câu hỏi
            return new MaterialDto(
                material.Id,
                material.Title,
                material.TopicId,
                material.ExtractedText.Length,
                !string.IsNullOrWhiteSpace(material.ExtractedText)
            );
        }
    }

    // --- UC06: Giảng viên duyệt / sửa / xóa câu hỏi ---
    public class QuestionService
    {
        private readonly LmsDbContext _db;
        public QuestionService(LmsDbContext db) => _db = db;

        public async Task<List<QuestionDto>> GetQuestionsByMaterialAsync(int materialId, string status, int lecturerId)
        {
            var material = await _db.LearningMaterials.Include(m => m.Course).FirstOrDefaultAsync(m => m.Id == materialId)
                ?? throw new AppException(404, "Không tìm thấy tài liệu");
            if (material.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course");

            return await _db.Questions
                .Include(q => q.Options)
                .Where(q => q.MaterialId == materialId && q.Status == status)
                .Select(q => new QuestionDto(
                    q.Id, q.Content, q.Explanation, q.Status, q.Source,
                    q.Options.Select(o => new OptionDto(o.Id, o.Content, o.IsCorrect)).ToList()
                )).ToListAsync();
        }

        public async Task<QuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionDto dto, int lecturerId)
        {
            var q = await _db.Questions.Include(x => x.Course).Include(x => x.Options)
                .FirstOrDefaultAsync(x => x.Id == questionId) ?? throw new AppException(404, "Không tìm thấy câu hỏi");

            if (q.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course"); // UC06 alt branch

            q.Content = dto.Content;
            q.Explanation = dto.Explanation;

            _db.AnswerOptions.RemoveRange(q.Options);
            q.Options = dto.Options.Select(o => new AnswerOption { Content = o.Content, IsCorrect = o.IsCorrect }).ToList();

            await _db.SaveChangesAsync();
            return new QuestionDto(q.Id, q.Content, q.Explanation, q.Status, q.Source,
                q.Options.Select(o => new OptionDto(o.Id, o.Content, o.IsCorrect)).ToList());
        }

        public async Task ApproveAsync(int questionId, int lecturerId)
        {
            var q = await _db.Questions.Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == questionId)
                ?? throw new AppException(404, "Không tìm thấy câu hỏi");

            if (q.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course");

            if (q.Status != "Draft")
                throw new AppException(409, "Trạng thái câu hỏi không hợp lệ (chỉ duyệt câu hỏi đang ở trạng thái Draft)"); // UC06

            q.Status = "Approved";
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int questionId, int lecturerId)
        {
            var q = await _db.Questions.Include(x => x.Course).Include(x => x.Options)
                .FirstOrDefaultAsync(x => x.Id == questionId) ?? throw new AppException(404, "Không tìm thấy câu hỏi");

            if (q.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course");

            _db.AnswerOptions.RemoveRange(q.Options);
            _db.Questions.Remove(q);
            await _db.SaveChangesAsync();
        }
    }

    // --- UC07 & UC08: Tạo/Publish Quiz & Sinh viên làm/nộp Quiz ---
    public class QuizService
    {
        private readonly LmsDbContext _db;
        public QuizService(LmsDbContext db) => _db = db;

        public async Task<QuizDto> CreateQuizAsync(CreateQuizDto dto, int lecturerId)
        {
            var course = await _db.Courses.FindAsync(dto.CourseId) ?? throw new AppException(404, "Không tìm thấy khóa học");
            if (course.LecturerId != lecturerId) throw new AppException(403, "Không phải owner của Course");

            var quiz = new Quiz { CourseId = dto.CourseId, Title = dto.Title, Status = "Draft" };
            _db.Quizzes.Add(quiz);
            await _db.SaveChangesAsync();
            return new QuizDto(quiz.Id, quiz.CourseId, quiz.Title, quiz.Status);
        }

        public async Task<QuizDto> PublishAsync(int quizId, List<int> questionIds, int lecturerId)
        {
            var quiz = await _db.Quizzes.Include(q => q.Course).FirstOrDefaultAsync(q => q.Id == quizId)
                ?? throw new AppException(404, "Không tìm thấy Quiz");

            if (quiz.Course.LecturerId != lecturerId)
                throw new AppException(403, "Không phải owner của Course");

            if (quiz.Status == "Published")
                throw new AppException(409, "Quiz đã được publish trước đó"); // UC07 alt branch

            if (questionIds == null || questionIds.Count == 0)
                throw new AppException(400, "Quiz phải có ít nhất một câu hỏi đã duyệt"); // UC07 alt branch

            var questions = await _db.Questions.Where(q => questionIds.Contains(q.Id)).ToListAsync();
            if (questions.Count != questionIds.Distinct().Count() ||
                questions.Any(q => q.Status != "Approved" || q.CourseId != quiz.CourseId))
            {
                throw new AppException(400, "Quiz phải có ít nhất một câu hỏi đã duyệt và thuộc đúng khóa học"); // UC07
            }

            foreach (var qId in questionIds.Distinct())
            {
                _db.QuizQuestions.Add(new QuizQuestion { QuizId = quizId, QuestionId = qId });
            }

            quiz.Status = "Published";
            quiz.PublishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return new QuizDto(quiz.Id, quiz.CourseId, quiz.Title, quiz.Status);
        }

        // UC08 Bước 1: Tải đề bài - Loại bỏ hoàn toàn trường IsCorrect
        public async Task<QuizTakeDto> GetQuizForStudentAsync(int quizId, int studentId)
        {
            var quiz = await _db.Quizzes
                .Include(q => q.QuizQuestions)
                .ThenInclude(qq => qq.Question)
                .ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == quizId);

            if (quiz == null || quiz.Status != "Published")
                throw new AppException(404, "Quiz chưa được publish hoặc không tồn tại"); // UC08

            bool isEnrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == quiz.CourseId && e.StudentId == studentId);
            if (!isEnrolled)
                throw new AppException(403, "Bạn chưa ghi danh vào khóa học này"); // UC08

            var questionsDto = quiz.QuizQuestions.Select(qq => new StudentQuestionTakeDto(
                qq.Question.Id,
                qq.Question.Content,
                qq.Question.Options.Select(o => new StudentOptionTakeDto(o.Id, o.Content)).ToList()
            )).ToList();

            return new QuizTakeDto(quiz.Id, quiz.Title, questionsDto);
        }

        // UC08 Bước 2: Nộp bài và tự động chấm điểm tại Backend
        public async Task<SubmitResultDto> SubmitAsync(int quizId, List<SubmitAnswerItemDto> answers, int studentId)
        {
            bool alreadySubmitted = await _db.QuizAttempts
                .AnyAsync(a => a.QuizId == quizId && a.StudentId == studentId && a.Status == "Submitted");
            if (alreadySubmitted)
                throw new AppException(409, "Bạn đã nộp bài này rồi"); // UC08 alt branch

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var attempt = new QuizAttempt
                {
                    QuizId = quizId,
                    StudentId = studentId,
                    Status = "InProgress"
                };
                _db.QuizAttempts.Add(attempt);
                await _db.SaveChangesAsync();

                var questionIds = answers.Select(a => a.QuestionId).ToList();
                var standardOptions = await _db.AnswerOptions
                    .Where(o => questionIds.Contains(o.QuestionId))
                    .ToListAsync();

                int correctCount = 0;
                foreach (var ans in answers)
                {
                    var matchedOption = standardOptions.FirstOrDefault(o => o.Id == ans.SelectedOptionId && o.QuestionId == ans.QuestionId);
                    if (matchedOption == null)
                        throw new AppException(400, "Option không thuộc Question tương ứng"); // Rollback ngay khi sai

                    bool isCorrect = matchedOption.IsCorrect; // Server tự quyết định từ CSDL
                    if (isCorrect) correctCount++;

                    _db.StudentAnswers.Add(new StudentAnswer
                    {
                        AttemptId = attempt.Id,
                        QuestionId = ans.QuestionId,
                        SelectedOptionId = ans.SelectedOptionId,
                        IsCorrect = isCorrect
                    });
                }

                // Công thức tính điểm chuẩn trong biểu đồ tuần tự UC08: (số câu đúng / tổng số câu) * 10
                decimal score = answers.Count > 0
                    ? Math.Round(((decimal)correctCount / answers.Count) * 10m, 2)
                    : 0m;

                attempt.Score = score;
                attempt.Status = "Submitted";
                attempt.SubmittedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return new SubmitResultDto(attempt.Id, score, correctCount, answers.Count);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
