namespace LexiFlow.Services;

/// <summary>
/// Lightweight, per-user motivation metrics stored on the device.
/// Server progress remains the source of truth for spaced repetition; these values
/// exist only to make each study action feel immediate (daily goal, XP and level).
/// </summary>
public sealed class LearningMetricsService
{
    private const int XpPerLevel = 200;
    private readonly SessionService _session;

    public LearningMetricsService(SessionService session)
    {
        _session = session;
    }

    public int DailyGoal => 10;

    public int TodayReviews
    {
        get
        {
            EnsureToday();
            return Preferences.Get(Key("today_reviews"), 0);
        }
    }

    public int TotalXp => Preferences.Get(Key("total_xp"), 0);
    public int Level => (TotalXp / XpPerLevel) + 1;
    public int XpInLevel => TotalXp % XpPerLevel;
    public int XpToNextLevel => XpPerLevel - XpInLevel;
    public double DailyGoalProgress => Math.Clamp((double)TodayReviews / DailyGoal, 0, 1);
    public double LevelProgress => (double)XpInLevel / XpPerLevel;

    public int RegisterReview(bool correct)
    {
        EnsureToday();

        Preferences.Set(Key("today_reviews"), TodayReviews + 1);
        var gained = correct ? 12 : 4;
        Preferences.Set(Key("total_xp"), TotalXp + gained);
        return gained;
    }

    private void EnsureToday()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (Preferences.Get(Key("today"), "") == today)
            return;

        Preferences.Set(Key("today"), today);
        Preferences.Set(Key("today_reviews"), 0);
    }

    private string Key(string suffix)
    {
        var user = string.IsNullOrWhiteSpace(_session.CurrentUserId)
            ? "guest"
            : _session.CurrentUserId.Trim().ToLowerInvariant();
        return $"learning_{user}_{suffix}";
    }
}
