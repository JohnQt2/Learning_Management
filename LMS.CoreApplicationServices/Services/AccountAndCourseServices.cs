using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Data;
using LMS.Core.Infrastructure.Entities;
using LMS.CoreApplicationServices.DTOs;
using Microsoft.EntityFrameworkCore;

namespace LMS.CoreApplicationServices.Services
{
    // --- UC01: Đăng nhập & Xác thực JWT ---
    public class AuthService
    {
        private readonly LmsDbContext _db;
        private readonly IJwtTokenGenerator _jwt;

        public AuthService(LmsDbContext db, IJwtTokenGenerator jwt)
        {
            _db = db;
            _jwt = jwt;
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
            // Không phân biệt "sai username" và "sai password" hoặc bị khóa để tránh lộ thông tin
            if (user == null || user.IsLocked || user.PasswordHash != dto.Password)
                throw new AppException(401, "Thông tin đăng nhập không đúng");

            var token = _jwt.GenerateToken(user.Id, user.Role);
            return new LoginResponseDto(token, user.Id, user.FullName, user.Role);
        }
    }

    // --- UC10: Quản trị viên quản lý tài khoản ---
    public class UserService
    {
        private readonly LmsDbContext _db;
        public UserService(LmsDbContext db) => _db = db;

        public async Task<PagedResult<UserDto>> GetUsersAsync(string? role, int page = 1, int pageSize = 10)
        {
            var query = _db.Users.AsQueryable();
            if (!string.IsNullOrEmpty(role))
                query = query.Where(u => u.Role == role);

            var total = await query.CountAsync();
            var items = await query
                .OrderBy(u => u.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsLocked))
                .ToListAsync();

            return new PagedResult<UserDto>(items, total, page, pageSize);
        }

        public async Task SetLockStateAsync(int targetUserId, bool isLocked, int currentAdminId)
        {
            if (targetUserId == currentAdminId && isLocked)
                throw new AppException(400, "Không thể tự khóa tài khoản của bạn"); // UC10 alt branch

            var user = await _db.Users.FindAsync(targetUserId)
                ?? throw new AppException(404, "Không tìm thấy tài khoản");

            user.IsLocked = isLocked;
            await _db.SaveChangesAsync();
        }
    }

    // --- UC02 & UC03: Tạo khóa học & Tham gia khóa học ---
    public class CourseService
    {
        private readonly LmsDbContext _db;
        public CourseService(LmsDbContext db) => _db = db;

        public async Task<CourseDto> CreateCourseAsync(CreateCourseDto dto, int lecturerId)
        {
            bool isDuplicated = await _db.Courses.AnyAsync(c => c.Code == dto.Code);
            if (isDuplicated)
                throw new AppException(409, "Mã môn học đã tồn tại"); // UC02 alt branch

            string joinCode = Guid.NewGuid().ToString("N")[..6].ToUpper();
            var course = new Course
            {
                Code = dto.Code,
                Title = dto.Title,
                Description = dto.Description,
                LecturerId = lecturerId,
                JoinCode = joinCode
            };

            _db.Courses.Add(course);
            await _db.SaveChangesAsync();
            return new CourseDto(course.Id, course.Code, course.Title, course.JoinCode);
        }

        public async Task<TopicDto> CreateTopicAsync(int courseId, CreateTopicDto dto, int lecturerId)
        {
            var course = await _db.Courses.FindAsync(courseId) ?? throw new AppException(404, "Không tìm thấy khóa học");
            if (course.LecturerId != lecturerId) throw new AppException(403, "Không phải giảng viên sở hữu khóa học");

            var topic = new Topic { CourseId = courseId, Name = dto.Name };
            _db.Topics.Add(topic);
            await _db.SaveChangesAsync();
            return new TopicDto(topic.Id, topic.Name);
        }

        public async Task<EnrollmentDto> EnrollAsync(int courseId, string joinCode, int studentId)
        {
            var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
            if (course == null || !string.Equals(course.JoinCode, joinCode, StringComparison.OrdinalIgnoreCase))
                throw new AppException(404, "Mã tham gia không hợp lệ"); // UC03 alt branch

            bool alreadyEnrolled = await _db.Enrollments.AnyAsync(e => e.CourseId == courseId && e.StudentId == studentId);
            if (alreadyEnrolled)
                throw new AppException(409, "Bạn đã tham gia khóa học này"); // UC03 alt branch

            var enrollment = new Enrollment
            {
                CourseId = courseId,
                StudentId = studentId,
                EnrolledAt = DateTime.UtcNow
            };

            _db.Enrollments.Add(enrollment);
            await _db.SaveChangesAsync();
            return new EnrollmentDto(enrollment.CourseId, enrollment.StudentId, enrollment.EnrolledAt);
        }
    }
}
