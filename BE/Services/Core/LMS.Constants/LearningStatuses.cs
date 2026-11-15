namespace LMS.Constants;

/// <summary>Trạng thái câu hỏi trong ngân hàng câu hỏi (cột Questions.Status, tối đa 20 ký tự).</summary>
public static class QuestionStatus
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    /// <summary>Câu Draft bị giảng viên loại bỏ: ẩn khỏi danh sách chọn Quiz, không xóa khỏi CSDL.</summary>
    public const string Rejected = "Rejected";
}

/// <summary>Nguồn tạo câu hỏi (cột Questions.Source).</summary>
public static class QuestionSource
{
    public const string Manual = "Manual";
    public const string AI = "AI";
}

/// <summary>Trạng thái Quiz (cột Quizzes.Status, tối đa 20 ký tự).</summary>
public static class QuizStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    /// <summary>Quiz đã phát hành nhưng không nhận bài nộp mới; vẫn tính vào tiến độ và thống kê.</summary>
    public const string Closed = "Closed";

    /// <summary>Quiz đã đến tay sinh viên (Published hoặc Closed).</summary>
    public static bool IsReleased(string status) => status is Published or Closed;
}

/// <summary>Trạng thái lần làm bài (cột QuizAttempts.Status).</summary>
public static class AttemptStatus
{
    public const string Submitted = "Submitted";
}
