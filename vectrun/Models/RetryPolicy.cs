namespace vectrun.Models;

internal record RetryPolicy
{
    public int RetryCount { get; init; }
    public int RetryDelayMs { get; init; }
    public string DelayType { get; init; } = "linear"; // "linear" | "sliding"
}
