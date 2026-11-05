using System.Text.Json;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Dtos;
using LMS.CoreApplicationServices.CourseModule.Implements;
using LMS.CoreApplicationServices.QuestionModule.Dtos;
using LMS.CoreApplicationServices.QuestionModule.Implements;
using LMS.CoreApplicationServices.QuizModule.Dtos;
using LMS.CoreApplicationServices.QuizModule.Implements;
using LMS.CoreApplicationServices.ResultModule.Implements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

internal static class BusinessSmoke
{
    private static int _checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _checks++;
        Console.WriteLine("PASS: " + label);
    }
    private static async Task Reject(Func<Task> action, int status, string label)
    {
        try { await action(); throw new Exception("FAIL: accepted " + label); }
        catch (AppException ex) when (ex.StatusCode == status) { Check(true, label); }
    }
    private static List<OptionInput> Options() => [new("A", true), new("B", false), new("C", false), new("D", false)];
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-business-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "business.sqlite")};Pooling=False;Default Timeout=15";
        var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).Options;
        try
        {
            await using var db = new LmsDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var lecturer = new User { Username = "owner", FullName = "Owner", Role = "Lecturer" };
            var outsider = new User { Username = "other-teacher", FullName = "Other", Role = "Lecturer" };
            var student = new User { Username = "learner", FullName = "Learner", Role = "Student" };
            var otherStudent = new User { Username = "other-learner", FullName = "Other Learner", Role = "Student" };
            db.Users.AddRange(lecturer, outsider, student, otherStudent);
            await db.SaveChangesAsync();
            var courses = new CourseService(db);
            var questions = new QuestionService(db, courses);
            var quizzes = new QuizService(db, courses);
            var results = new ResultService(db);
            var course = await courses.CreateAsync(lecturer.Id, new("scope101", "Scope", "Description"), default);
            await Reject(() => courses.CreateAsync(lecturer.Id, new(" SCOPE101 ", "Duplicate", null), default), 409, "Course codes normalize and reject duplicates");
            await Reject(() => courses.UpdateAsync(outsider.Id, course.Id, new("Stolen", null), default), 403, "Other lecturer cannot edit course");
            await Reject(() => courses.UpdateAsync(lecturer.Id, course.Id, new("Valid", new string('x', 2001)), default), 400, "Course update bounds description");
            await Reject(() => courses.GetAsync(course.Id, student.Id, "Student", default), 403, "Course detail requires enrollment");
            var codes = new HashSet<string> { course.JoinCode! };
            for (var i = 0; i < 16; i++)
                Check(codes.Add((await courses.CreateAsync(lecturer.Id, new($"UNIQUE{i}", "Code test", null), default)).JoinCode!), "Generated join code is unique " + i);
            await courses.EnrollByCodeAsync(student.Id, new(" " + course.JoinCode!.ToLowerInvariant() + " "), default);
            Check((await courses.GetAsync(course.Id, student.Id, "Student", default)).JoinCode is null, "Student course detail hides JoinCode");
            Check((await courses.GetAsync(course.Id, lecturer.Id, "Lecturer", default)).EnrollmentCount == 1, "Course detail reports actual enrollment count");
            await Reject(() => courses.EnrollByCodeAsync(student.Id, new(course.JoinCode!), default), 409, "Join-code-only workflow rejects duplicate membership");
            await Reject(() => courses.EnrollByCodeAsync(student.Id, new(""), default), 400, "Empty JoinCode rejected");
            var topic = await courses.CreateTopicAsync(course.Id, lecturer.Id, new("Old topic"), default);
            Check((await courses.UpdateTopicAsync(course.Id, topic.Id, lecturer.Id, new("Edited topic"), default)).Name == "Edited topic", "Owner updates topic");
            await Reject(() => courses.UpdateTopicAsync(course.Id, topic.Id, outsider.Id, new("Stolen"), default), 403, "Other lecturer cannot edit topic");
            var otherCourse = await courses.CreateAsync(outsider.Id, new("OTHER", "Other course", null), default);
            var foreignTopic = await courses.CreateTopicAsync(otherCourse.Id, outsider.Id, new("Foreign topic"), default);
            await Reject(() => courses.UpdateTopicAsync(course.Id, foreignTopic.Id, lecturer.Id, new("Moved"), default), 404, "Topic ID cannot cross course boundary");
            await Reject(() => questions.CreateAsync(lecturer.Id, new(course.Id, foreignTopic.Id, "Bad scope", null, Options()), default), 400, "Question topic must belong to course");
            await Reject(() => questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, "Question", null, [null!, new("B", false), new("C", false), new("D", false)]), default), 400, "Null option returns validation error");
            await Reject(() => questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, "Question", null, [new("A", true), new(" A ", false), new("C", false), new("D", false)]), default), 400, "Duplicate normalized answer choices rejected");
            await Reject(() => questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, " ", null, Options()), default), 400, "Blank question rejected");
            await Reject(() => questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, "Question", new string('x', 4001), Options()), default), 400, "Question explanation length enforced");
            var draft = await questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, "Before edit", "Explanation", Options()), default);
            var edited = await questions.EditAsync(draft.Id, lecturer.Id, new("After edit", "Reviewed", Options()), default);
            Check(edited.Content == "After edit" && edited.Options.Count == 4, "Question draft edit replaces four options correctly");
            var approved = new List<QuestionDto> { await questions.ApproveAsync(draft.Id, lecturer.Id, default) };
            for (var i = 1; i < 5; i++)
            {
                var q = await questions.CreateAsync(lecturer.Id, new(course.Id, topic.Id, "Question " + i, "Explanation " + i, Options()), default);
                approved.Add(await questions.ApproveAsync(q.Id, lecturer.Id, default));
            }
            var invalid = new Question { CourseId = course.Id, TopicId = topic.Id, Content = " ", Options = Options().Select(o => new AnswerOption { Content = o.Content, IsCorrect = o.IsCorrect }).ToList() };
            db.Questions.Add(invalid);
            await db.SaveChangesAsync();
            await Reject(() => questions.ApproveAsync(invalid.Id, lecturer.Id, default), 400, "Approval validates legacy blank question content");
            await Reject(() => questions.ListAsync(course.Id, outsider.Id, default), 403, "Question answer bank restricted to course owner");
            var foreign = await questions.CreateAsync(outsider.Id, new(otherCourse.Id, foreignTopic.Id, "Other question", null, Options()), default);
            foreign = await questions.ApproveAsync(foreign.Id, outsider.Id, default);
            var quiz = await quizzes.CreateAsync(lecturer.Id, new(course.Id, "Five question quiz", "Description", approved.Select(q => q.Id).ToList()), default);
            var authoring = await quizzes.AuthoringAsync(quiz.Id, lecturer.Id, default);
            Check(authoring.QuestionIds.Count == 5 && authoring.Questions.Count == 5 && authoring.Description == "Description", "Draft preserves description and selected question preview");
            await Reject(() => quizzes.AuthoringAsync(quiz.Id, outsider.Id, default), 403, "Quiz authoring protects answers from other lecturer");
            await Reject(() => quizzes.TakeAsync(quiz.Id, student.Id, default), 404, "Student cannot take Draft quiz");
            await Reject(() => quizzes.PublishAsync(quiz.Id, lecturer.Id, new([]), default), 400, "Cannot publish empty quiz");
            await Reject(() => quizzes.PublishAsync(quiz.Id, lecturer.Id, new([approved[0].Id, approved[0].Id]), default), 400, "Cannot publish duplicate questions");
            await Reject(() => quizzes.PublishAsync(quiz.Id, lecturer.Id, new([invalid.Id]), default), 400, "Cannot publish Draft question");
            await Reject(() => quizzes.PublishAsync(quiz.Id, lecturer.Id, new([foreign.Id]), default), 400, "Cannot publish foreign-course question");
            await quizzes.UpdateAsync(quiz.Id, lecturer.Id, new("Subset draft", "Temporary selection", approved.Take(2).Select(q => q.Id).ToList()), default);
            Check((await quizzes.AuthoringAsync(quiz.Id, lecturer.Id, default)).QuestionIds.Count == 2,
                "Draft selection removes deselected questions from database");
            await quizzes.UpdateAsync(quiz.Id, lecturer.Id, new("Updated title", "Updated description", approved.Select(q => q.Id).ToList()), default);
            await quizzes.PublishAsync(quiz.Id, lecturer.Id, new(approved.Select(q => q.Id).ToList()), default);
            Check((await quizzes.ListAsync(course.Id, student.Id, "Student", default)).Single().QuestionCount == 5,
                "Quiz list reports actual saved question count");
            await Reject(() => quizzes.UpdateAsync(quiz.Id, lecturer.Id, new("Tamper", null, []), default), 409, "Published quiz is immutable");
            var examJson = JsonSerializer.Serialize(await quizzes.TakeAsync(quiz.Id, student.Id, default));
            Check(!examJson.Contains("IsCorrect", StringComparison.OrdinalIgnoreCase) && !examJson.Contains("Explanation", StringComparison.OrdinalIgnoreCase), "Take contract has neither answers nor explanation");
            await Reject(() => quizzes.SubmitAsync(quiz.Id, otherStudent.Id, new([]), default), 403, "Unenrolled student cannot submit quiz");
            await Reject(() => quizzes.SubmitAsync(quiz.Id, student.Id, new(null!), default), 400, "Null answers rejected");
            await Reject(() => quizzes.SubmitAsync(quiz.Id, student.Id, new([null!, null!, null!, null!, null!]), default), 400, "Null answer entries rejected");
            await Reject(() => quizzes.SubmitAsync(quiz.Id, student.Id, new([]), default), 400, "Missing answers rejected");
            var correctAnswers = approved.Select(q => new SubmitItem(q.Id, q.Options.Single(o => o.IsCorrect).Id)).ToList();
            await Reject(() => quizzes.SubmitAsync(quiz.Id, student.Id, new(correctAnswers.Select((a, i) => i == 0 ? new SubmitItem(a.QuestionId, foreign.Options[0].Id) : a).ToList()), default), 400, "Forged selected option from another question rejected");
            await Reject(() => quizzes.SubmitAsync(quiz.Id, student.Id, new(correctAnswers.Select((a, i) => i == 4 ? correctAnswers[0] : a).ToList()), default), 400, "Duplicate answer IDs rejected");
            var sixtyAnswers = approved.Select((q, i) => new SubmitItem(q.Id, q.Options.First(o => o.IsCorrect == (i < 3)).Id)).ToList();
            var attempt = await quizzes.SubmitAsync(quiz.Id, student.Id, new(sixtyAnswers), default);
            Check(attempt.Score == 6m && attempt.CorrectCount == 3 && attempt.TotalQuestions == 5, "Five-answer score calculated only by backend");
            var result = await results.GetAsync(attempt.AttemptId, student.Id, "Student", default);
            Check(result.Answers.Count == 5 && result.WrongCount == 2 && result.Accuracy == 60 && !result.TopicAccuracies.Single().IsWeak, "Result feedback and exact 60 percent boundary are correct");
            Check((await results.MyRecommendationsAsync(student.Id, default)).Count == 0, "Exactly 60 percent has no weak recommendation");
            await Reject(() => results.GetAsync(attempt.AttemptId, otherStudent.Id, "Student", default), 403, "Student cannot read another student's result");
            await Reject(() => results.GetAsync(attempt.AttemptId, outsider.Id, "Lecturer", default), 403, "Other lecturer cannot read course results");
            Check((await results.CourseAttemptsAsync(course.Id, lecturer.Id, default)).Count == 1, "Owner sees course attempt table");
            await Reject(() => results.CourseAttemptsAsync(course.Id, outsider.Id, default), 403, "Other lecturer cannot list course attempts");
            var weakQuiz = await quizzes.CreateAsync(lecturer.Id, new(course.Id, "Weak topic quiz"), default);
            await quizzes.PublishAsync(weakQuiz.Id, lecturer.Id, new([approved[0].Id]), default);
            await quizzes.SubmitAsync(weakQuiz.Id, student.Id, new([new(approved[0].Id, approved[0].Options.First(o => !o.IsCorrect).Id)]), default);
            var weakRecommendations = await results.MyRecommendationsAsync(student.Id, default);
            Check(weakRecommendations.Count == 1 && weakRecommendations[0].Accuracy == 50 && weakRecommendations[0].MaterialId is null,
                "Cumulative accuracy combines attempts and shows weak topic with no material");
            db.LearningMaterials.Add(new LearningMaterial { CourseId = course.Id, TopicId = topic.Id, Title = "Review", StoragePath = "test-only.txt" });
            await db.SaveChangesAsync();
            var withMaterial = await results.MyRecommendationsAsync(student.Id, default);
            Check(withMaterial.Count == 1 && withMaterial[0].MaterialId is not null && withMaterial[0].DownloadUrl!.StartsWith("/api/materials/"), "Aggregate recommendations find actual topic material without duplicate rows");
            await using (var reopened = new LmsDbContext(options))
                Check(await reopened.QuizAttempts.CountAsync(a => a.StudentId == student.Id) == 2 && await reopened.StudentAnswers.CountAsync() == 6,
                    "Quiz history and answers persist after DbContext restart");
            await RaceSubmissionAsync(connectionString, lecturer.Id, student.Id, course.Id, approved[0]);
            await RaceEnrollmentAsync(connectionString, course.Id, course.JoinCode!, otherStudent.Id);
            await StaleDraftTestsAsync(options, lecturer.Id, course.Id, topic.Id, approved[0]);
            var enrollment = await db.Enrollments.SingleAsync(e => e.StudentId == student.Id && e.CourseId == course.Id);
            db.Enrollments.Remove(enrollment);
            await db.SaveChangesAsync();
            Check((await results.MyRecommendationsAsync(student.Id, default)).Count == 0, "Recommendations exclude courses after enrollment is removed");
            Console.WriteLine($"BUSINESS SMOKE: {_checks} checks passed using isolated SQLite file.");
        }
        finally
        {
            // This directory was created by this test and never references the workspace data.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task RaceSubmissionAsync(string connectionString, int lecturerId, int studentId, int courseId, QuestionDto question)
    {
        var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).Options;
        int quizId;
        await using (var setup = new LmsDbContext(options))
        {
            var quizzes = new QuizService(setup, new CourseService(setup));
            quizId = (await quizzes.CreateAsync(lecturerId, new(courseId, "Concurrent submission"), default)).Id;
            await quizzes.PublishAsync(quizId, lecturerId, new([question.Id]), default);
        }
        var barrier = new SaveBarrier(typeof(QuizAttempt));
        var racingOptions = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).AddInterceptors(barrier).Options;
        async Task<int> Submit()
        {
            await using var db = new LmsDbContext(racingOptions);
            try
            {
                await new QuizService(db, new CourseService(db)).SubmitAsync(quizId, studentId,
                    new([new(question.Id, question.Options.Single(o => o.IsCorrect).Id)]), default);
                return 200;
            }
            catch (AppException ex) { return ex.StatusCode; }
        }
        var statuses = await Task.WhenAll(Task.Run(Submit), Task.Run(Submit));
        Check(statuses.Count(s => s == 200) == 1 && statuses.Count(s => s == 409) == 1 && barrier.Arrivals == 2,
            "Simultaneous submits pass precheck together; one success and one conflict");
        await using var verify = new LmsDbContext(options);
        Check(await verify.QuizAttempts.CountAsync(a => a.QuizId == quizId && a.StudentId == studentId) == 1 &&
            await verify.StudentAnswers.CountAsync(a => a.Attempt.QuizId == quizId) == 1, "Concurrent submit stores one attempt and one answer graph");
    }
    private static async Task RaceEnrollmentAsync(string connectionString, int courseId, string code, int studentId)
    {
        var barrier = new SaveBarrier(typeof(Enrollment));
        var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).AddInterceptors(barrier).Options;
        async Task<int> Enroll()
        {
            await using var db = new LmsDbContext(options);
            try { await new CourseService(db).EnrollAsync(studentId, courseId, new(code), default); return 200; }
            catch (AppException ex) { return ex.StatusCode; }
        }
        var statuses = await Task.WhenAll(Task.Run(Enroll), Task.Run(Enroll));
        Check(statuses.Count(s => s == 200) == 1 && statuses.Count(s => s == 409) == 1 && barrier.Arrivals == 2,
            "Simultaneous enrollments accept one and reject the unique-index race");
    }
    private static async Task StaleDraftTestsAsync(DbContextOptions<LmsDbContext> options, int lecturerId, int courseId, int topicId, QuestionDto approved)
    {
        await using var stale = new LmsDbContext(options);
        await using var fresh = new LmsDbContext(options);
        var staleQuestions = new QuestionService(stale, new CourseService(stale));
        var freshQuestions = new QuestionService(fresh, new CourseService(fresh));
        var draft = await staleQuestions.CreateAsync(lecturerId, new(courseId, topicId, "Original concurrent question", "Original", Options()), default);
        await freshQuestions.ApproveAsync(draft.Id, lecturerId, default);
        try
        {
            await staleQuestions.EditAsync(draft.Id, lecturerId, new("Original concurrent question", "Original",
                [new("A", false), new("B", true), new("C", false), new("D", false)]), default);
            throw new Exception("FAIL: stale edit changed approved question");
        }
        catch (DbUpdateConcurrencyException) { Check(true, "Stale options-only Draft edit cannot overwrite concurrent question approval"); }
        catch (AppException ex) when (ex.StatusCode == 409) { Check(true, "Stale options-only Draft edit cannot overwrite concurrent question approval"); }
        stale.ChangeTracker.Clear();
        var storedQuestion = await stale.Questions.AsNoTracking().Include(q => q.Options).SingleAsync(q => q.Id == draft.Id);
        Check(storedQuestion.Content == "Original concurrent question" && storedQuestion.Status == "Approved" && storedQuestion.Options.Count == 4 && storedQuestion.Options.Single(o => o.IsCorrect).Content == "A",
            "Question concurrency failure rolls option replacement back");
        var staleQuizService = new QuizService(stale, new CourseService(stale));
        var freshQuizService = new QuizService(fresh, new CourseService(fresh));
        var quiz = await staleQuizService.CreateAsync(lecturerId, new(courseId, "Original concurrent quiz", "Original", [approved.Id]), default);
        await freshQuizService.PublishAsync(quiz.Id, lecturerId, new([approved.Id]), default);
        try
        {
            await staleQuizService.UpdateAsync(quiz.Id, lecturerId, new("Original concurrent quiz", "Original", []), default);
            throw new Exception("FAIL: stale edit changed published quiz");
        }
        catch (DbUpdateConcurrencyException) { Check(true, "Stale selection-only Draft quiz edit cannot overwrite concurrent publication"); }
        catch (AppException ex) when (ex.StatusCode == 409) { Check(true, "Stale selection-only Draft quiz edit cannot overwrite concurrent publication"); }
        stale.ChangeTracker.Clear();
        var storedQuiz = await stale.Quizzes.AsNoTracking().Include(q => q.QuizQuestions).SingleAsync(q => q.Id == quiz.Id);
        Check(storedQuiz.Status == "Published" && storedQuiz.Title == "Original concurrent quiz" && storedQuiz.QuizQuestions.Count == 1,
            "Quiz concurrency failure preserves published question selection");
    }

    private sealed class SaveBarrier(Type entityType) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => _arrivals;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(e => e.Entity.GetType() == entityType && e.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
