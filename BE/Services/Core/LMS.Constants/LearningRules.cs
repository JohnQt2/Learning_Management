namespace LMS.Constants;

/// <summary>Quy tắc nghiệp vụ dùng chung giữa QuizModule và ResultModule.</summary>
public static class LearningRules
{
    /// <summary>Topic được coi là yếu khi tỷ lệ trả lời đúng nhỏ hơn ngưỡng này (60%).</summary>
    public const double WeakTopicThreshold = 0.60;

    /// <summary>Số học liệu tối đa được gợi ý cho mỗi Topic yếu.</summary>
    public const int MaxRecommendationsPerTopic = 2;

    /// <summary>Thang điểm của một lần làm bài.</summary>
    public const decimal MaxScore = 10m;
}
