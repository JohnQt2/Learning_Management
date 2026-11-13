using System.Text.Json;

namespace LMS.Core.Infrastructure.Adapters;

public interface IAIClient
{
    Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, CancellationToken ct);
    Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, AiRequestBudget budget, CancellationToken ct)
    {
        budget.Reserve(ct);
        return GenerateJsonAsync(sourceText, count, difficulty, ct);
    }
}

public sealed class MockAiClient : IAIClient
{
    public Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sentences = sourceText.Split(new[] { '.', '\n', '!', '?' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 8).ToArray();
        if (sentences.Length == 0) sentences = new[] { sourceText.Trim() };
        var rows = Enumerable.Range(0, count).Select(i =>
        {
            var sentence = sentences[i % sentences.Length];
            var excerpt = sentence[..Math.Min(180, sentence.Length)];
            if (excerpt.Length < sentence.Length && char.IsHighSurrogate(excerpt[^1])) excerpt = excerpt[..^1];
            return new
            {
                content = $"Theo đoạn “{excerpt}”, phát biểu nào phù hợp nhất? (Câu {i + 1}, {difficulty})",
                explanation = "Câu hỏi demo dùng trích đoạn tài liệu, giảng viên cần rà soát và duyệt.",
                options = new[]
                {
                    new { content = excerpt, isCorrect = true },
                    new { content = "Nội dung không liên quan đến tài liệu", isCorrect = false },
                    new { content = "Bỏ qua khái niệm quan trọng của chủ đề", isCorrect = false },
                    new { content = "Không có thông tin nào trong tài liệu", isCorrect = false },
                }
            };
        }).ToArray();
        return Task.FromResult(JsonSerializer.Serialize(rows));
    }
}

/// <summary>Explicitly unavailable AI. Manual question authoring remains fully functional.</summary>
public sealed class DisabledAiClient : IAIClient
{
    public Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, CancellationToken ct)
        => throw new AiClientException(503, "AI chưa được cấu hình trên Backend");
}
