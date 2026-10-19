using System.Linq;
using LMS.Core.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS.Core.Infrastructure.Data
{
    public class LmsDbContext : DbContext
    {
        public LmsDbContext(DbContextOptions<LmsDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<Topic> Topics => Set<Topic>();
        public DbSet<LearningMaterial> LearningMaterials => Set<LearningMaterial>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
        public DbSet<AIGenerationLog> AIGenerationLogs => Set<AIGenerationLog>();
        public DbSet<Quiz> Quizzes => Set<Quiz>();
        public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
        public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
        public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
        public DbSet<MaterialRecommendation> MaterialRecommendations => Set<MaterialRecommendation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Ràng buộc UNIQUE theo đúng thiết kế CSDL
            modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
            modelBuilder.Entity<Course>().HasIndex(c => c.Code).IsUnique();

            // Chốt chặn cuối cùng ở tầng CSDL: UNIQUE (CourseId, StudentId)
            modelBuilder.Entity<Enrollment>()
                .HasIndex(e => new { e.CourseId, e.StudentId })
                .IsUnique();

            modelBuilder.Entity<QuizQuestion>()
                .HasKey(qq => new { qq.QuizId, qq.QuestionId });

            modelBuilder.Entity<QuizAttempt>()
                .Property(a => a.Score)
                .HasPrecision(5, 2);

            // SQL Server không cho phép nhiều đường cascade delete (multiple cascade paths),
            // ví dụ: User -> Course -> Enrollment và User -> Enrollment.
            // Mặc định chuyển toàn bộ FK sang Restrict, sau đó chỉ bật Cascade cho bảng con thuần túy.
            foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
                fk.DeleteBehavior = DeleteBehavior.Restrict;

            modelBuilder.Entity<AnswerOption>()
                .HasOne(o => o.Question).WithMany(q => q.Options)
                .HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuizQuestion>()
                .HasOne(qq => qq.Quiz).WithMany(q => q.QuizQuestions)
                .HasForeignKey(qq => qq.QuizId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentAnswer>()
                .HasOne(sa => sa.Attempt).WithMany(a => a.StudentAnswers)
                .HasForeignKey(sa => sa.AttemptId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
