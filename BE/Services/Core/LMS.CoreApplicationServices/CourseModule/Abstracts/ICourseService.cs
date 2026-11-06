using LMS.Core.Domain.Entities;
using LMS.CoreApplicationServices.CourseModule.Dtos;
namespace LMS.CoreApplicationServices.CourseModule.Abstracts;
public interface ICourseService
{
    Task<List<CourseDto>> ListAsync(int userId, string role, CancellationToken ct);
    Task<CourseDto> GetAsync(int courseId, int userId, string role, CancellationToken ct);
    Task<CourseDto> UpdateAsync(int lecturerId, int courseId, CourseUpdateRequest request, CancellationToken ct);
    Task<List<EnrollmentStudentDto>> StudentsAsync(int lecturerId, int courseId, CancellationToken ct);
    Task<CourseDto> CreateAsync(int lecturerId, CourseCreateRequest request, CancellationToken ct);
    Task<EnrollmentDto> EnrollAsync(int studentId, int courseId, EnrollmentRequest request, CancellationToken ct);
    Task<EnrollmentDto> EnrollByCodeAsync(int studentId, EnrollmentRequest request, CancellationToken ct);
    Task<Course> RequireOwnerAsync(int courseId, int lecturerId, CancellationToken ct);
    Task RequireMemberAsync(int courseId, int userId, string role, CancellationToken ct);
    Task<List<TopicDto>> ListTopicsAsync(int courseId, int userId, string role, CancellationToken ct);
    Task<TopicDto> CreateTopicAsync(int courseId, int lecturerId, TopicRequest request, CancellationToken ct);
    Task<TopicDto> UpdateTopicAsync(int courseId, int topicId, int lecturerId, TopicRequest request, CancellationToken ct);
}
