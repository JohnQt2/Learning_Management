using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LMS.CoreApplicationServices.Validators
{
    public class ParsedOption
    {
        public string Content { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    public class ParsedQuestion
    {
        public string Content { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public List<ParsedOption> Options { get; set; } = new();
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public List<ParsedQuestion> Questions { get; set; } = new();
    }

    public class QuestionJsonValidator
    {
        public ValidationResult ParseAndValidate(string rawJson, int expectedCount)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return new ValidationResult { IsValid = false, ErrorMessage = "Output từ AI rỗng." };

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<List<ParsedQuestion>>(rawJson, options);

                if (parsed == null || parsed.Count == 0)
                    return new ValidationResult { IsValid = false, ErrorMessage = "JSON không chứa câu hỏi nào." };

                foreach (var q in parsed)
                {
                    if (string.IsNullOrWhiteSpace(q.Content))
                        return new ValidationResult { IsValid = false, ErrorMessage = "Có câu hỏi bị rỗng nội dung." };

                    if (q.Options == null || q.Options.Count < 2)
                        return new ValidationResult { IsValid = false, ErrorMessage = "Mỗi câu hỏi phải có ít nhất 2 lựa chọn (options)." };

                    if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Content)))
                        return new ValidationResult { IsValid = false, ErrorMessage = "Nội dung phương án trả lời không được rỗng." };

                    // Kiểm tra có đúng 1 đáp án đúng
                    if (q.Options.Count(o => o.IsCorrect) != 1)
                        return new ValidationResult { IsValid = false, ErrorMessage = "Mỗi câu hỏi bắt buộc phải có đúng 1 đáp án đúng." };
                }

                return new ValidationResult { IsValid = true, Questions = parsed };
            }
            catch (JsonException ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"Sai cú pháp JSON: {ex.Message}" };
            }
        }
    }
}
