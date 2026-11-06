namespace LMS.CoreApplicationServices.QuestionModule.Dtos;
public record OptionInput(string Content, bool IsCorrect);
public record QuestionCreateRequest(int CourseId, int TopicId, string Content, string? Explanation, List<OptionInput> Options, string? Difficulty = null);
public record QuestionEditRequest(string Content, string? Explanation, List<OptionInput> Options, string? Difficulty = null);
public record QuestionDto(int Id, int CourseId, int TopicId, int? MaterialId, string Content, string Explanation, string Status, string Source, List<OptionDisplay> Options,
    string? Difficulty = null, int? SourceChunkIndex = null, int? SourceStartCharacter = null, int? SourceEndCharacter = null);
public record OptionDisplay(int Id, string Content, bool IsCorrect);
