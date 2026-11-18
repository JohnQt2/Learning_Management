using System.Globalization;
using LMS.Constants;

namespace LMS.CoreApplicationServices.ResultModule.Implements;

/// <summary>
/// Quy tắc gợi ý ôn tập dùng chung: QuizService gọi khi lưu gợi ý lúc nộp bài,
/// ResultService gọi khi tính kết quả, dashboard và thống kê. Đổi ngưỡng ở LearningRules là đủ.
/// </summary>
public static class RecommendationPolicy
{
    /// <summary>Số học liệu tối đa được gợi ý cho mỗi Topic yếu.</summary>
    public const int MaxMaterialsPerTopic = LearningRules.MaxRecommendationsPerTopic;

    /// <summary>Topic yếu khi tỷ lệ đúng nhỏ hơn ngưỡng; Topic chưa có câu trả lời không bị coi là yếu.</summary>
    public static bool IsWeak(int correct, int total)
        => total > 0 && (double)correct / total < LearningRules.WeakTopicThreshold;

    /// <summary>Ngưỡng hiển thị cho người dùng, ví dụ "60%".</summary>
    public static string ThresholdText
        => (LearningRules.WeakTopicThreshold * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
