using System;
using System.Collections.Generic;

namespace LMS.Core.Infrastructure.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = "Student"; // "Admin", "Lecturer", "Student"
        public bool IsLocked { get; set; } = false;
    }

    public class Course
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string JoinCode { get; set; } = string.Empty;
        public int LecturerId { get; set; }
        public User Lecturer { get; set; } = null!;
        public ICollection<Topic> Topics { get; set; } = new List<Topic>();
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }

    public class Enrollment
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
        public int StudentId { get; set; }
        public User Student { get; set; } = null!;
        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    }

    public class Topic
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
    }

    public class LearningMaterial
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
        public int TopicId { get; set; }
        public Topic Topic { get; set; } = null!;
        public string Title { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
    }

    public class Question
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
        public int TopicId { get; set; }
        public Topic Topic { get; set; } = null!;
        public int? MaterialId { get; set; }
        public LearningMaterial? Material { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string Source { get; set; } = "Manual"; // "AI" hoặc "Manual"
        public string Status { get; set; } = "Draft";  // "Draft" hoặc "Approved"
        public ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
    }

    public class AnswerOption
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
        public string Content { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class AIGenerationLog
    {
        public int Id { get; set; }
        public int MaterialId { get; set; }
        public string Status { get; set; } = string.Empty; // "Success" hoặc "Failed"
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Quiz
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; } = null!;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft"; // "Draft" hoặc "Published"
        public DateTime? PublishedAt { get; set; }
        public ICollection<QuizQuestion> QuizQuestions { get; set; } = new List<QuizQuestion>();
    }

    public class QuizQuestion
    {
        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;
        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
    }

    public class QuizAttempt
    {
        public int Id { get; set; }
        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;
        public int StudentId { get; set; }
        public User Student { get; set; } = null!;
        public decimal Score { get; set; }
        public string Status { get; set; } = "InProgress"; // "InProgress" hoặc "Submitted"
        public DateTime? SubmittedAt { get; set; }
        public ICollection<StudentAnswer> StudentAnswers { get; set; } = new List<StudentAnswer>();
    }

    public class StudentAnswer
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public QuizAttempt Attempt { get; set; } = null!;
        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
        public int SelectedOptionId { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class MaterialRecommendation
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public int TopicId { get; set; }
        public int MaterialId { get; set; }
        public LearningMaterial Material { get; set; } = null!;
    }
}
