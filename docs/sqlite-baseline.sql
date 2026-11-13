CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Users" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "Username" TEXT NOT NULL,
    "FullName" TEXT NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Role" TEXT NOT NULL,
    "IsLocked" INTEGER NOT NULL
);

CREATE TABLE "Courses" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Courses" PRIMARY KEY AUTOINCREMENT,
    "Code" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "JoinCode" TEXT NOT NULL,
    "LecturerId" INTEGER NOT NULL,
    CONSTRAINT "FK_Courses_Users_LecturerId" FOREIGN KEY ("LecturerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Enrollments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Enrollments" PRIMARY KEY AUTOINCREMENT,
    "CourseId" INTEGER NOT NULL,
    "StudentId" INTEGER NOT NULL,
    "EnrolledAt" TEXT NOT NULL,
    CONSTRAINT "FK_Enrollments_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Enrollments_Users_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Quizzes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quizzes" PRIMARY KEY AUTOINCREMENT,
    "CourseId" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "PublishedAt" TEXT NULL,
    CONSTRAINT "FK_Quizzes_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Topics" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Topics" PRIMARY KEY AUTOINCREMENT,
    "CourseId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    CONSTRAINT "FK_Topics_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "QuizAttempts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_QuizAttempts" PRIMARY KEY AUTOINCREMENT,
    "QuizId" INTEGER NOT NULL,
    "StudentId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Score" TEXT NOT NULL,
    "SubmittedAt" TEXT NOT NULL,
    CONSTRAINT "FK_QuizAttempts_Quizzes_QuizId" FOREIGN KEY ("QuizId") REFERENCES "Quizzes" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_QuizAttempts_Users_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "LearningMaterials" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LearningMaterials" PRIMARY KEY AUTOINCREMENT,
    "CourseId" INTEGER NOT NULL,
    "TopicId" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "OriginalFileName" TEXT NOT NULL,
    "StoragePath" TEXT NOT NULL,
    "ExtractedText" TEXT NOT NULL,
    CONSTRAINT "FK_LearningMaterials_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_LearningMaterials_Topics_TopicId" FOREIGN KEY ("TopicId") REFERENCES "Topics" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AIGenerationLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AIGenerationLogs" PRIMARY KEY AUTOINCREMENT,
    "MaterialId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "ErrorMessage" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_AIGenerationLogs_LearningMaterials_MaterialId" FOREIGN KEY ("MaterialId") REFERENCES "LearningMaterials" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "MaterialRecommendations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MaterialRecommendations" PRIMARY KEY AUTOINCREMENT,
    "AttemptId" INTEGER NOT NULL,
    "MaterialId" INTEGER NOT NULL,
    "TopicId" INTEGER NOT NULL,
    CONSTRAINT "FK_MaterialRecommendations_LearningMaterials_MaterialId" FOREIGN KEY ("MaterialId") REFERENCES "LearningMaterials" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MaterialRecommendations_QuizAttempts_AttemptId" FOREIGN KEY ("AttemptId") REFERENCES "QuizAttempts" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Questions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Questions" PRIMARY KEY AUTOINCREMENT,
    "CourseId" INTEGER NOT NULL,
    "TopicId" INTEGER NOT NULL,
    "MaterialId" INTEGER NULL,
    "Content" TEXT NOT NULL,
    "Explanation" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    CONSTRAINT "FK_Questions_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Questions_LearningMaterials_MaterialId" FOREIGN KEY ("MaterialId") REFERENCES "LearningMaterials" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Questions_Topics_TopicId" FOREIGN KEY ("TopicId") REFERENCES "Topics" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AnswerOptions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AnswerOptions" PRIMARY KEY AUTOINCREMENT,
    "QuestionId" INTEGER NOT NULL,
    "Content" TEXT NOT NULL,
    "IsCorrect" INTEGER NOT NULL,
    CONSTRAINT "FK_AnswerOptions_Questions_QuestionId" FOREIGN KEY ("QuestionId") REFERENCES "Questions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "QuizQuestions" (
    "QuizId" INTEGER NOT NULL,
    "QuestionId" INTEGER NOT NULL,
    CONSTRAINT "PK_QuizQuestions" PRIMARY KEY ("QuizId", "QuestionId"),
    CONSTRAINT "FK_QuizQuestions_Questions_QuestionId" FOREIGN KEY ("QuestionId") REFERENCES "Questions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_QuizQuestions_Quizzes_QuizId" FOREIGN KEY ("QuizId") REFERENCES "Quizzes" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "StudentAnswers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StudentAnswers" PRIMARY KEY AUTOINCREMENT,
    "AttemptId" INTEGER NOT NULL,
    "QuestionId" INTEGER NOT NULL,
    "SelectedOptionId" INTEGER NOT NULL,
    "IsCorrect" INTEGER NOT NULL,
    CONSTRAINT "FK_StudentAnswers_AnswerOptions_SelectedOptionId" FOREIGN KEY ("SelectedOptionId") REFERENCES "AnswerOptions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_StudentAnswers_Questions_QuestionId" FOREIGN KEY ("QuestionId") REFERENCES "Questions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_StudentAnswers_QuizAttempts_AttemptId" FOREIGN KEY ("AttemptId") REFERENCES "QuizAttempts" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AIGenerationLogs_MaterialId" ON "AIGenerationLogs" ("MaterialId");

CREATE INDEX "IX_AnswerOptions_QuestionId" ON "AnswerOptions" ("QuestionId");

CREATE UNIQUE INDEX "IX_Courses_Code" ON "Courses" ("Code");

CREATE UNIQUE INDEX "IX_Courses_JoinCode" ON "Courses" ("JoinCode");

CREATE INDEX "IX_Courses_LecturerId" ON "Courses" ("LecturerId");

CREATE UNIQUE INDEX "IX_Enrollments_CourseId_StudentId" ON "Enrollments" ("CourseId", "StudentId");

CREATE INDEX "IX_Enrollments_StudentId" ON "Enrollments" ("StudentId");

CREATE INDEX "IX_LearningMaterials_CourseId" ON "LearningMaterials" ("CourseId");

CREATE INDEX "IX_LearningMaterials_TopicId" ON "LearningMaterials" ("TopicId");

CREATE INDEX "IX_MaterialRecommendations_AttemptId" ON "MaterialRecommendations" ("AttemptId");

CREATE INDEX "IX_MaterialRecommendations_MaterialId" ON "MaterialRecommendations" ("MaterialId");

CREATE INDEX "IX_Questions_CourseId" ON "Questions" ("CourseId");

CREATE INDEX "IX_Questions_MaterialId" ON "Questions" ("MaterialId");

CREATE INDEX "IX_Questions_TopicId" ON "Questions" ("TopicId");

CREATE UNIQUE INDEX "IX_QuizAttempts_QuizId_StudentId" ON "QuizAttempts" ("QuizId", "StudentId");

CREATE INDEX "IX_QuizAttempts_StudentId" ON "QuizAttempts" ("StudentId");

CREATE INDEX "IX_QuizQuestions_QuestionId" ON "QuizQuestions" ("QuestionId");

CREATE INDEX "IX_Quizzes_CourseId" ON "Quizzes" ("CourseId");

CREATE INDEX "IX_StudentAnswers_AttemptId" ON "StudentAnswers" ("AttemptId");

CREATE INDEX "IX_StudentAnswers_QuestionId" ON "StudentAnswers" ("QuestionId");

CREATE INDEX "IX_StudentAnswers_SelectedOptionId" ON "StudentAnswers" ("SelectedOptionId");

CREATE INDEX "IX_Topics_CourseId" ON "Topics" ("CourseId");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261023130419_InitialBaseline', '10.0.10');

COMMIT;

