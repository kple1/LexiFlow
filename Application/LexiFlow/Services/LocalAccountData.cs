namespace LexiFlow.Services;

// Numeric server identity prevents case variants and re-registered names sharing local data.
public static class LocalAccountData
{
    public static string Key(SessionService session, string suffix) => $"account_{session.StorageId}_{suffix}";

    public static void MigrateLegacy(int id, string userId, string? lastSignedInUser)
    {
        // Only import the legacy device owner's data after that exact account authenticates.
        if (!string.Equals(userId, lastSignedInUser, StringComparison.Ordinal)) return;
        var prefix = $"account_{id}_";
        if (Preferences.ContainsKey(prefix + "migrated_from")) return;
        var old = userId.Trim().ToLowerInvariant();
        CopyString($"sentence_archive_{old}_v1", prefix + "archive");
        CopyString($"sentence_lesson_history_{old}_v3", prefix + "history");
        CopyString($"course_v1_{Uri.EscapeDataString(userId)}_stages", prefix + "course_stages");
        CopyString($"course_v1_{Uri.EscapeDataString(userId)}_stars", prefix + "course_stars");
        CopyString($"learning_{old}_today", prefix + "today");
        CopyString("streak_last_date", prefix + "streak_last_date");
        CopyInt($"learning_{old}_today_reviews", prefix + "today_reviews");
        CopyInt($"learning_{old}_total_xp", prefix + "total_xp");
        CopyInt("streak_count", prefix + "streak_count");
        Preferences.Set(prefix + "migrated_from", userId);
    }

    public static void DeleteCurrent(SessionService session)
    {
        var legacyOwner = Preferences.Get(Key(session, "migrated_from"), "");
        if (!string.IsNullOrEmpty(legacyOwner))
        {
            var old = legacyOwner.Trim().ToLowerInvariant();
            foreach (var key in new[] { $"sentence_archive_{old}_v1", $"sentence_lesson_history_{old}_v3",
                $"course_v1_{Uri.EscapeDataString(legacyOwner)}_stages", $"course_v1_{Uri.EscapeDataString(legacyOwner)}_stars",
                $"learning_{old}_today", $"learning_{old}_today_reviews", $"learning_{old}_total_xp",
                "streak_last_date", "streak_count" }) LargePreferenceStore.Remove(key);
        }
        foreach (var suffix in new[] { "archive", "history", "course_stages", "course_stars", "sentence_review", "builtin_word_progress_v1", "today",
            "today_reviews", "total_xp", "streak_count", "streak_last_date", "migrated_from" })
            LargePreferenceStore.Remove(Key(session, suffix));
        foreach (var word in BuiltInVocabulary.GetWords())
            Preferences.Remove(Key(session, $"builtin_word_progress_v1:{word.Id}"));
    }

    private static void CopyString(string source, string target)
    {
        if (Preferences.ContainsKey(source) && !LargePreferenceStore.Contains(target))
            Preferences.Set(target, Preferences.Get(source, ""));
    }
    private static void CopyInt(string source, string target)
    {
        if (Preferences.ContainsKey(source) && !Preferences.ContainsKey(target))
            Preferences.Set(target, Preferences.Get(source, 0));
    }
}
