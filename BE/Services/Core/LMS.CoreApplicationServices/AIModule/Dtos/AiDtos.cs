namespace LMS.CoreApplicationServices.AIModule.Dtos;
public record GenerateRequest(int MaterialId, int Count, string Difficulty);
public record AiChunkPlanDto(int Index, int StartCharacter, int EndCharacter, int QuestionCount);
public record AiGenerationPlanDto(int MaterialId, int TotalSourceCharacters, int ChunkCount, int ProviderRequestLimit,
    int MaxRetries, int GenerationTimeoutSeconds, List<AiChunkPlanDto> Chunks);
public sealed class AiOption { public string Content { get; set; } = ""; public bool IsCorrect { get; set; } }
public sealed class AiQuestion { public string Content { get; set; } = ""; public string Explanation { get; set; } = ""; public List<AiOption> Options { get; set; } = new(); }
