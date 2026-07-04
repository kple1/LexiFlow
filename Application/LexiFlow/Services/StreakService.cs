namespace LexiFlow.Services;

// Tracks a consecutive-day study streak locally (no backend needed).
public class StreakService
{
    private const string LastDateKey = "streak_last_date";
    private const string CountKey = "streak_count";

    // Current streak. A streak survives if the user studied today or yesterday;
    // a longer gap means it's broken and reads as 0 until they study again.
    public int Current
    {
        get
        {
            var count = Preferences.Get(CountKey, 0);
            if (!DateTime.TryParse(Preferences.Get(LastDateKey, ""), out var last))
                return 0;
            var gap = (DateTime.Now.Date - last.Date).Days;
            return gap <= 1 ? count : 0;
        }
    }

    // Advances the streak at most once per calendar day. Call on any study action.
    public void RegisterStudyToday()
    {
        var today = DateTime.Now.Date;
        DateTime.TryParse(Preferences.Get(LastDateKey, ""), out var last);
        if (last.Date == today)
            return; // already counted today

        var count = Preferences.Get(CountKey, 0);
        count = last.Date == today.AddDays(-1) ? count + 1 : 1; // consecutive day, else restart
        Preferences.Set(CountKey, count);
        Preferences.Set(LastDateKey, today.ToString("yyyy-MM-dd"));
    }
}
