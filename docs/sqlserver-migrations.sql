IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(80) NOT NULL,
        [FullName] nvarchar(max) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [IsLocked] bit NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Courses] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(40) NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [JoinCode] nvarchar(12) NOT NULL,
        [LecturerId] int NOT NULL,
        CONSTRAINT [PK_Courses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Courses_Users_LecturerId] FOREIGN KEY ([LecturerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Enrollments] (
        [Id] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [StudentId] int NOT NULL,
        [EnrolledAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Enrollments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Enrollments_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Enrollments_Users_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Quizzes] (
        [Id] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PublishedAt] datetime2 NULL,
        CONSTRAINT [PK_Quizzes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Quizzes_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Topics] (
        [Id] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_Topics] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Topics_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [QuizAttempts] (
        [Id] int NOT NULL IDENTITY,
        [QuizId] int NOT NULL,
        [StudentId] int NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [Score] decimal(5,2) NOT NULL,
        [SubmittedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_QuizAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_QuizAttempts_Quizzes_QuizId] FOREIGN KEY ([QuizId]) REFERENCES [Quizzes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QuizAttempts_Users_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [LearningMaterials] (
        [Id] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [TopicId] int NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [OriginalFileName] nvarchar(max) NOT NULL,
        [StoragePath] nvarchar(max) NOT NULL,
        [ExtractedText] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_LearningMaterials] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LearningMaterials_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LearningMaterials_Topics_TopicId] FOREIGN KEY ([TopicId]) REFERENCES [Topics] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [AIGenerationLogs] (
        [Id] int NOT NULL IDENTITY,
        [MaterialId] int NOT NULL,
        [Status] nvarchar(max) NOT NULL,
        [ErrorMessage] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AIGenerationLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AIGenerationLogs_LearningMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [LearningMaterials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [MaterialRecommendations] (
        [Id] int NOT NULL IDENTITY,
        [AttemptId] int NOT NULL,
        [MaterialId] int NOT NULL,
        [TopicId] int NOT NULL,
        CONSTRAINT [PK_MaterialRecommendations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaterialRecommendations_LearningMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [LearningMaterials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MaterialRecommendations_QuizAttempts_AttemptId] FOREIGN KEY ([AttemptId]) REFERENCES [QuizAttempts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [Questions] (
        [Id] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [TopicId] int NOT NULL,
        [MaterialId] int NULL,
        [Content] nvarchar(max) NOT NULL,
        [Explanation] nvarchar(max) NOT NULL,
        [Source] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_Questions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Questions_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Questions_LearningMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [LearningMaterials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Questions_Topics_TopicId] FOREIGN KEY ([TopicId]) REFERENCES [Topics] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [AnswerOptions] (
        [Id] int NOT NULL IDENTITY,
        [QuestionId] int NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [IsCorrect] bit NOT NULL,
        CONSTRAINT [PK_AnswerOptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AnswerOptions_Questions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [Questions] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [QuizQuestions] (
        [QuizId] int NOT NULL,
        [QuestionId] int NOT NULL,
        CONSTRAINT [PK_QuizQuestions] PRIMARY KEY ([QuizId], [QuestionId]),
        CONSTRAINT [FK_QuizQuestions_Questions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [Questions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QuizQuestions_Quizzes_QuizId] FOREIGN KEY ([QuizId]) REFERENCES [Quizzes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE TABLE [StudentAnswers] (
        [Id] int NOT NULL IDENTITY,
        [AttemptId] int NOT NULL,
        [QuestionId] int NOT NULL,
        [SelectedOptionId] int NOT NULL,
        [IsCorrect] bit NOT NULL,
        CONSTRAINT [PK_StudentAnswers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StudentAnswers_AnswerOptions_SelectedOptionId] FOREIGN KEY ([SelectedOptionId]) REFERENCES [AnswerOptions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StudentAnswers_Questions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [Questions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StudentAnswers_QuizAttempts_AttemptId] FOREIGN KEY ([AttemptId]) REFERENCES [QuizAttempts] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_AIGenerationLogs_MaterialId] ON [AIGenerationLogs] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_AnswerOptions_QuestionId] ON [AnswerOptions] ([QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Courses_Code] ON [Courses] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Courses_JoinCode] ON [Courses] ([JoinCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Courses_LecturerId] ON [Courses] ([LecturerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Enrollments_CourseId_StudentId] ON [Enrollments] ([CourseId], [StudentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Enrollments_StudentId] ON [Enrollments] ([StudentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_LearningMaterials_CourseId] ON [LearningMaterials] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_LearningMaterials_TopicId] ON [LearningMaterials] ([TopicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_MaterialRecommendations_AttemptId] ON [MaterialRecommendations] ([AttemptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_MaterialRecommendations_MaterialId] ON [MaterialRecommendations] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Questions_CourseId] ON [Questions] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Questions_MaterialId] ON [Questions] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Questions_TopicId] ON [Questions] ([TopicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_QuizAttempts_QuizId_StudentId] ON [QuizAttempts] ([QuizId], [StudentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_QuizAttempts_StudentId] ON [QuizAttempts] ([StudentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_QuizQuestions_QuestionId] ON [QuizQuestions] ([QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Quizzes_CourseId] ON [Quizzes] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_StudentAnswers_AttemptId] ON [StudentAnswers] ([AttemptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_StudentAnswers_QuestionId] ON [StudentAnswers] ([QuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_StudentAnswers_SelectedOptionId] ON [StudentAnswers] ([SelectedOptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE INDEX [IX_Topics_CourseId] ON [Topics] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130430_InitialBaseline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023130430_InitialBaseline', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130720_AccountSecurityAndQuizDescription'
)
BEGIN
    ALTER TABLE [Users] ADD [SecurityVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130720_AccountSecurityAndQuizDescription'
)
BEGIN
    ALTER TABLE [Quizzes] ADD [Description] nvarchar(2000) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023130720_AccountSecurityAndQuizDescription'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023130720_AccountSecurityAndQuizDescription', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190002_UserProfileMetadata'
)
BEGIN
    ALTER TABLE [Users] ADD [CreatedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190002_UserProfileMetadata'
)
BEGIN
    ALTER TABLE [Users] ADD [Email] nvarchar(254) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190002_UserProfileMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028190002_UserProfileMetadata', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [ContentType] nvarchar(150) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [ExtractionMessage] nvarchar(500) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [ExtractionStatus] nvarchar(30) NOT NULL DEFAULT N'Legacy';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [FileFormat] nvarchar(16) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [SizeBytes] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [UploadedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD [UploadedById] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    CREATE INDEX [IX_LearningMaterials_UploadedById] ON [LearningMaterials] ([UploadedById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    ALTER TABLE [LearningMaterials] ADD CONSTRAINT [FK_LearningMaterials_Users_UploadedById] FOREIGN KEY ([UploadedById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028190321_MaterialExtractionMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028190321_MaterialExtractionMetadata', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [Questions] ADD [Difficulty] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [Questions] ADD [SourceChunkIndex] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [Questions] ADD [SourceEndCharacter] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [Questions] ADD [SourceStartCharacter] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [AIGenerationLogs] ADD [ChunkCount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [AIGenerationLogs] ADD [DurationMilliseconds] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [AIGenerationLogs] ADD [GeneratedQuestionCount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [AIGenerationLogs] ADD [ProviderRequestCount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    ALTER TABLE [AIGenerationLogs] ADD [RequestedQuestionCount] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029053820_AiGenerationProvenance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029053820_AiGenerationProvenance', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029054534_QuizRevision'
)
BEGIN
    ALTER TABLE [Quizzes] ADD [Revision] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029054534_QuizRevision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029054534_QuizRevision', N'10.0.10');
END;

COMMIT;
GO

