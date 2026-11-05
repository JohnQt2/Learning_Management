using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.Common;
using Microsoft.Extensions.Configuration;

namespace LMS.CoreApplicationServices.AIModule.Implements;

public sealed record AiGenerationLimits(int MaxSourceCharacters, int MaxChunks, int MaxRequests, int MaxRetries, int TotalTimeoutSeconds)
{
    public static AiGenerationLimits From(IConfiguration configuration) => new(
        Math.Clamp(configuration.GetValue("Ai:MaxSourceCharacters", 16000), 1000, 50000),
        Math.Clamp(configuration.GetValue("Ai:MaxGenerationChunks", 3), 1, 5),
        Math.Clamp(configuration.GetValue("Ai:MaxProviderRequests", 4), 1, 12),
        Math.Clamp(configuration.GetValue("Ai:MaxRetries", 1), 0, 2),
        Math.Clamp(configuration.GetValue("Ai:GenerationTimeoutSeconds", 240), 5, 300));
}

public static class AiGenerationPlanner
{
    public static AiGenerationPlanDto Build(int materialId, string text, int count, AiGenerationLimits limits)
    {
        if (count is < 3 or > 10 || string.IsNullOrWhiteSpace(text))
            throw new AppException(400, "Cần văn bản trích xuất hợp lệ và từ 3 đến 10 câu hỏi");
        var maximumChunks = Math.Min(limits.MaxChunks, count);
        var ranges = new List<(int Start, int End)>();
        var start = 0;
        while (start < text.Length)
        {
            if (ranges.Count == maximumChunks)
                throw new AppException(400, $"Tài liệu vượt giới hạn {maximumChunks} đoạn, mỗi đoạn tối đa {limits.MaxSourceCharacters} ký tự. Hãy chia tài liệu; không phần nào được gửi tới AI.");
            var end = Math.Min(text.Length, start + limits.MaxSourceCharacters);
            if (end < text.Length)
            {
                var minimumEnd = text.Length - (maximumChunks - ranges.Count - 1) * limits.MaxSourceCharacters;
                var searchFrom = Math.Max(start + limits.MaxSourceCharacters * 3 / 4, minimumEnd - 1);
                if (searchFrom < end)
                {
                    var boundary = text.LastIndexOf('\n', end - 1, end - searchFrom);
                    if (boundary < searchFrom) boundary = text.LastIndexOf(' ', end - 1, end - searchFrom);
                    if (boundary >= searchFrom) end = boundary + 1;
                }
                if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
            }
            ranges.Add((start, end)); start = end;
        }
        if (ranges.Count > limits.MaxRequests)
            throw new AppException(400, "Số đoạn vượt ngân sách request AI. Hãy chia tài liệu hoặc điều chỉnh giới hạn có kiểm soát.");
        if (ranges.Any(range => string.IsNullOrWhiteSpace(text[range.Start..range.End])))
            throw new AppException(400, "Tài liệu có đoạn chỉ chứa khoảng trắng. Hãy chuẩn hóa tài liệu trước khi tạo câu hỏi; chưa có nội dung nào được gửi tới AI.");

        // Every source range receives a question; allocate the remainder by size.
        var quantities = Enumerable.Repeat(1, ranges.Count).ToArray();
        var remainder = count - ranges.Count;
        var shares = ranges.Select((range, index) => new { Index = index, Weight = (double)(range.End - range.Start) * remainder / text.Length }).ToList();
        foreach (var share in shares) quantities[share.Index] += (int)Math.Floor(share.Weight);
        var left = count - quantities.Sum();
        foreach (var share in shares.OrderByDescending(x => x.Weight - Math.Floor(x.Weight)).ThenBy(x => x.Index).Take(left)) quantities[share.Index]++;
        return new(materialId, text.Length, ranges.Count, limits.MaxRequests, limits.MaxRetries, limits.TotalTimeoutSeconds,
            ranges.Select((range, index) => new AiChunkPlanDto(index + 1, range.Start, range.End, quantities[index])).ToList());
    }
}
