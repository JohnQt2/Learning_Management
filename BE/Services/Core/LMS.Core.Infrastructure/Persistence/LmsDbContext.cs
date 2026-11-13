using LMS.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS.Core.Infrastructure.Persistence;

public class LmsDbContext : DbContext
{
    public LmsDbContext(DbContextOptions<LmsDbContext> options) : base(options) { }
    protected LmsDbContext(DbContextOptions options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<LearningMaterial> LearningMaterials => Set<LearningMaterial>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
    public DbSet<MaterialRecommendation> MaterialRecommendations => Set<MaterialRecommendation>();
    public DbSet<AIGenerationLog> AIGenerationLogs => Set<AIGenerationLog>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        m.Entity<User>().HasIndex(x => x.Username).IsUnique();
        m.Entity<Course>().HasIndex(x => x.Code).IsUnique();
        m.Entity<Course>().HasIndex(x => x.JoinCode).IsUnique();
        m.Entity<Enrollment>().HasIndex(x => new { x.CourseId, x.StudentId }).IsUnique();
        m.Entity<QuizAttempt>().HasIndex(x => new { x.QuizId, x.StudentId }).IsUnique();
        m.Entity<QuizQuestion>().HasKey(x => new { x.QuizId, x.QuestionId });
        m.Entity<QuizAttempt>().Property(x => x.Score).HasPrecision(5, 2);
        m.Entity<User>().Property(x => x.Username).HasMaxLength(80).IsRequired();
        m.Entity<User>().Property(x => x.Role).HasMaxLength(20).IsRequired();
        m.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        m.Entity<User>().Property(x => x.SecurityVersion).IsConcurrencyToken().HasDefaultValue(0);
        m.Entity<Course>().Property(x => x.Code).HasMaxLength(40).IsRequired();
        m.Entity<Course>().Property(x => x.JoinCode).HasMaxLength(12).IsRequired();
        m.Entity<Question>().Property(x => x.Status).HasMaxLength(20).IsConcurrencyToken();
        m.Entity<Question>().Property(x => x.Difficulty).HasMaxLength(20);
        m.Entity<Quiz>().Property(x => x.Status).HasMaxLength(20).IsConcurrencyToken();
        m.Entity<Quiz>().Property(x => x.Revision).HasDefaultValue(0).IsConcurrencyToken();
        m.Entity<Quiz>().Property(x => x.Description).HasMaxLength(2000).HasDefaultValue("");
        m.Entity<LearningMaterial>().Property(x => x.FileFormat).HasMaxLength(16).HasDefaultValue("");
        m.Entity<LearningMaterial>().Property(x => x.ContentType).HasMaxLength(150).HasDefaultValue("");
        m.Entity<LearningMaterial>().Property(x => x.ExtractionStatus).HasMaxLength(30).HasDefaultValue("Legacy");
        m.Entity<LearningMaterial>().Property(x => x.ExtractionMessage).HasMaxLength(500).HasDefaultValue("");
        m.Entity<LearningMaterial>().Property(x => x.IsDeleted).HasDefaultValue(false).IsConcurrencyToken();

        // Prevent SQL Server multiple-cascade-path errors and protect historical results.
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
