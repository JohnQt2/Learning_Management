using System.Text.Json;
using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.Common;

namespace LMS.CoreApplicationServices.AIModule.Implements;

public static class QuestionJsonValidator
{
    public static List<AiQuestion> Parse(string json, int expectedCount)
    {
        if (expectedCount is < 1 or > 10 || string.IsNullOrWhiteSpace(json) || json.Length > 256_000)
            throw new AppException(422, "AI trả JSON hoặc số lượng không hợp lệ");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > expectedCount)
                throw new AppException(422, "AI trả cấu trúc hoặc số lượng không hợp lệ");
            var result = new List<AiQuestion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in root.EnumerateArray())
            {
                // Invalid rows are discarded independently; none can reach the question bank.
                if (row.ValueKind != JsonValueKind.Object || !HasExactFields(row, "content", "explanation", "options") ||
                    !ReadText(row, "content", 4000, out var content) || !ReadText(row, "explanation", 4000, out var explanation) ||
                    !row.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array || options.GetArrayLength() != 4)
                    continue;
                var parsed = new List<AiOption>();
                foreach (var option in options.EnumerateArray())
                    if (option.ValueKind == JsonValueKind.Object && HasExactFields(option, "content", "isCorrect") &&
                        ReadText(option, "content", 2000, out var optionText) && option.TryGetProperty("isCorrect", out var correct) &&
                        correct.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        parsed.Add(new AiOption { Content = optionText, IsCorrect = correct.GetBoolean() });
                if (parsed.Count != 4 || parsed.Count(o => o.IsCorrect) != 1 ||
                    parsed.Select(o => o.Content).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 || !seen.Add(content))
                    continue;
                result.Add(new AiQuestion { Content = content, Explanation = explanation, Options = parsed });
            }
            if (result.Count == 0) throw new AppException(422, "AI không trả câu hỏi hợp lệ; cần nội dung, giải thích, 4 phương án khác nhau và đúng 1 đáp án");
            return result;
        }
        catch (JsonException) { throw new AppException(422, "AI trả JSON sai định dạng"); }
    }

    private static bool HasExactFields(JsonElement row, params string[] fields)
    {
        var properties = row.EnumerateObject().ToList();
        return properties.Count == fields.Length && fields.All(field => properties.Count(p => p.NameEquals(field)) == 1);
    }

    private static bool ReadText(JsonElement row, string field, int maxLength, out string value)
    {
        value = "";
        if (!row.TryGetProperty(field, out var text) || text.ValueKind != JsonValueKind.String) return false;
        value = text.GetString()?.Trim() ?? "";
        return value.Length > 0 && value.Length <= maxLength;
    }
}
