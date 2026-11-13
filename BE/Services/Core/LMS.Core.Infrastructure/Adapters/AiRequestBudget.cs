namespace LMS.Core.Infrastructure.Adapters;

/// <summary>Shared across every chunk and retry in one generation request.</summary>
public sealed class AiRequestBudget
{
    private int _used;
    public int Maximum { get; }
    public int Used => Volatile.Read(ref _used);

    public AiRequestBudget(int maximum)
    {
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        Maximum = maximum;
    }

    public void Reserve(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var used = Used;
            if (used >= Maximum)
                throw new AiClientException(503, "Đã dùng hết giới hạn số lượt gọi AI cho yêu cầu này; hãy giảm số câu hỏi hoặc dung lượng tài liệu");
            if (Interlocked.CompareExchange(ref _used, used + 1, used) == used) return;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
