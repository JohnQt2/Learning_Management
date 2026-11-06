using System.Security.Cryptography;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.CourseModule.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.CourseModule.Implements;
public sealed class CourseService : ICourseService
{
    private readonly LmsDbContext _db;
    public CourseService(LmsDbContext db) => _db = db;
    private static CourseDto Map(Course c, bool maySeeCode, int count = 0) => new(c.Id, c.Code, c.Title, c.Description, maySeeCode ? c.JoinCode : null, c.LecturerId, count);

    public async Task<List<CourseDto>> ListAsync(int userId, string role, CancellationToken ct)
    {
        IQueryable<Course> courses = _db.Courses.AsNoTracking();
        if (role == "Lecturer") courses = courses.Where(x => x.LecturerId == userId);
        else if (role == "Student") courses = courses.Where(x => _db.Enrollments.Any(e => e.CourseId == x.Id && e.StudentId == userId));
        else if (role != "Admin") throw new AppException(403, "Không có quyền truy cập");
        return await courses.OrderByDescending(c => c.Id).Select(c => new CourseDto(c.Id, c.Code, c.Title,
            c.Description, role == "Lecturer" && c.LecturerId == userId ? c.JoinCode : null,
            c.LecturerId, _db.Enrollments.Count(e => e.CourseId == c.Id))).ToListAsync(ct);
    }
    public async Task<CourseDto> GetAsync(int courseId, int userId, string role, CancellationToken ct)
    {
        await RequireMemberAsync(courseId, userId, role, ct);
        var course = await _db.Courses.AsNoTracking().SingleAsync(c => c.Id == courseId, ct);
        return Map(course, role == "Lecturer" && course.LecturerId == userId,
            await _db.Enrollments.CountAsync(e => e.CourseId == courseId, ct));
    }
    public async Task<CourseDto> CreateAsync(int lecturerId, CourseCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) ||
            request.Code.Trim().Length > 40 || request.Title.Trim().Length > 150 || (request.Description?.Length ?? 0) > 2000)
            throw new AppException(400, "Mã/tên/mô tả khóa học không hợp lệ");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Courses.AnyAsync(x => x.Code.ToUpper() == code, ct)) throw new AppException(409, "CourseCode đã tồn tại");
        var joinCode = "";
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var retry = 0; retry < 8; retry++)
        {
            joinCode = new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
            if (!await _db.Courses.AnyAsync(c => c.JoinCode == joinCode, ct)) break;
        }
        if (await _db.Courses.AnyAsync(c => c.JoinCode == joinCode, ct)) throw new AppException(409, "Không tạo được JoinCode duy nhất");
        var course = new Course { LecturerId = lecturerId, Code = code, Title = request.Title.Trim(), Description = request.Description?.Trim() ?? "", JoinCode = joinCode };
        _db.Courses.Add(course);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            _db.Entry(course).State = EntityState.Detached;
            throw new AppException(409, "CourseCode hoặc JoinCode đã tồn tại; hãy thử lại");
        }
        return Map(course, true);
    }
    public async Task<CourseDto> UpdateAsync(int lecturerId, int courseId, CourseUpdateRequest request, CancellationToken ct)
    {
        var c = await RequireOwnerAsync(courseId, lecturerId, ct);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 150 || (request.Description?.Length ?? 0) > 2000)
            throw new AppException(400, "Tên/mô tả khóa học không hợp lệ");
        c.Title = request.Title.Trim(); c.Description = request.Description?.Trim() ?? "";
        await _db.SaveChangesAsync(ct);
        return Map(c, true, await _db.Enrollments.CountAsync(e => e.CourseId == courseId, ct));
    }
    public async Task<List<EnrollmentStudentDto>> StudentsAsync(int lecturerId, int courseId, CancellationToken ct)
    {
        await RequireOwnerAsync(courseId, lecturerId, ct);
        var published = await _db.Quizzes.CountAsync(q => q.CourseId == courseId && q.Status == "Published", ct);
        var enrollments = await _db.Enrollments.AsNoTracking().Where(x => x.CourseId == courseId)
            .OrderBy(x => x.Student.Username)
            .Select(x => new { x.StudentId, x.Student.Username, x.Student.FullName, x.EnrolledAt, x.Student.Email, x.Student.IsLocked })
            .ToListAsync(ct);
        var attempts = await _db.QuizAttempts.AsNoTracking().Where(a => a.Quiz.CourseId == courseId &&
                a.Quiz.Status == "Published" && a.Status == "Submitted")
            .Select(a => new { a.StudentId, a.QuizId, a.Score }).ToListAsync(ct);
        var byStudent = attempts.ToLookup(a => a.StudentId);
        return enrollments.Select(e =>
        {
            var scores = byStudent[e.StudentId].ToList();
            var completed = scores.Select(a => a.QuizId).Distinct().Count();
            return new EnrollmentStudentDto(e.StudentId, e.Username, e.FullName, e.EnrolledAt, e.Email, e.IsLocked,
                published, completed, published == 0 ? 0 : Math.Round(100.0 * completed / published, 1),
                scores.Count == 0 ? null : Math.Round(scores.Average(a => a.Score), 2),
                completed == 0 ? "NotStarted" : completed >= published ? "Completed" : "InProgress");
        }).ToList();
    }
    public async Task<EnrollmentDto> EnrollAsync(int studentId, int courseId, EnrollmentRequest request, CancellationToken ct)
    {
        var course = await _db.Courses.FindAsync(new object[] { courseId }, ct);
        if (course == null || !string.Equals(course.JoinCode, request.JoinCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new AppException(404, "JoinCode không hợp lệ");
        if (await _db.Enrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId, ct))
            throw new AppException(409, "Sinh viên đã tham gia khóa học");
        var enrollment = new Enrollment { CourseId = courseId, StudentId = studentId };
        _db.Enrollments.Add(enrollment);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            _db.Entry(enrollment).State = EntityState.Detached;
            throw new AppException(409, "Sinh viên đã tham gia khóa học");
        }
        return new(courseId, studentId, enrollment.EnrolledAt);
    }
    public async Task<EnrollmentDto> EnrollByCodeAsync(int studentId, EnrollmentRequest request, CancellationToken ct)
    {
        var code = request.JoinCode?.Trim().ToUpperInvariant() ?? "";
        if (code.Length is < 1 or > 12) throw new AppException(400, "Cần nhập JoinCode hợp lệ");
        var courseId = await _db.Courses.Where(c => c.JoinCode.ToUpper() == code).Select(c => (int?)c.Id).SingleOrDefaultAsync(ct)
            ?? throw new AppException(404, "JoinCode không hợp lệ");
        return await EnrollAsync(studentId, courseId, request, ct);
    }
    public async Task<Course> RequireOwnerAsync(int courseId, int lecturerId, CancellationToken ct)
    {
        var course = await _db.Courses.FindAsync(new object[] { courseId }, ct) ?? throw new AppException(404, "Không tìm thấy Course");
        if (course.LecturerId != lecturerId) throw new AppException(403, "Không phải chủ khóa học");
        return course;
    }
    public async Task RequireMemberAsync(int courseId, int userId, string role, CancellationToken ct)
    {
        if (!await _db.Courses.AnyAsync(c => c.Id == courseId, ct)) throw new AppException(404, "Không tìm thấy Course");
        if (role == "Admin") return;
        if (role == "Lecturer") { await RequireOwnerAsync(courseId, userId, ct); return; }
        if (role == "Student" && await _db.Enrollments.AnyAsync(x => x.CourseId == courseId && x.StudentId == userId, ct)) return;
        throw new AppException(403, "Chưa ghi danh hoặc không đủ quyền");
    }
    public async Task<List<TopicDto>> ListTopicsAsync(int courseId, int userId, string role, CancellationToken ct)
    {
        await RequireMemberAsync(courseId, userId, role, ct);
        return await _db.Topics.AsNoTracking().Where(t => t.CourseId == courseId)
            .OrderBy(t => t.Id).Select(t => new TopicDto(t.Id, t.CourseId, t.Name)).ToListAsync(ct);
    }
    public async Task<TopicDto> CreateTopicAsync(int courseId, int lecturerId, TopicRequest request, CancellationToken ct)
    {
        await RequireOwnerAsync(courseId, lecturerId, ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
            throw new AppException(400, "Tên Topic phải từ 1 đến 150 ký tự");
        var topic = new Topic { CourseId = courseId, Name = request.Name.Trim() };
        _db.Topics.Add(topic); await _db.SaveChangesAsync(ct);
        return new(topic.Id, topic.CourseId, topic.Name);
    }
    public async Task<TopicDto> UpdateTopicAsync(int courseId, int topicId, int lecturerId, TopicRequest request, CancellationToken ct)
    {
        await RequireOwnerAsync(courseId, lecturerId, ct);
        var topic = await _db.Topics.SingleOrDefaultAsync(t => t.Id == topicId && t.CourseId == courseId, ct)
            ?? throw new AppException(404, "Không tìm thấy Topic trong khóa học");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
            throw new AppException(400, "Tên Topic phải từ 1 đến 150 ký tự");
        topic.Name = request.Name.Trim();
        await _db.SaveChangesAsync(ct);
        return new(topic.Id, topic.CourseId, topic.Name);
    }
}
