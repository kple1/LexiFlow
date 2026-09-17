using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace LexiFlow.Services;

/// <summary>
/// Manages the optional daily review reminder. Permission is only requested from
/// an explicit user action in AccountView, never while the home screen is loading.
/// </summary>
public sealed class NotificationService
{
    private const int DailyReminderId = 1001;

    public async Task<bool> AreNotificationsEnabledAsync()
    {
        try
        {
            return await LocalNotificationCenter.Current.AreNotificationsEnabled();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnableDailyReminderAsync(int dueCount = 0)
    {
        try
        {
            var enabled = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            if (!enabled)
                enabled = await LocalNotificationCenter.Current.RequestNotificationPermission();

            if (!enabled)
                return false;

            await ScheduleDailyReminderAsync(dueCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ScheduleDailyReminderAsync(int dueCount)
    {
        try
        {
            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
                return;

            var body = dueCount > 0
                ? $"기억이 흐려지기 전에 {dueCount}개를 복습해 볼까요?"
                : "오늘의 짧은 복습으로 학습 흐름을 이어가세요.";

            var request = new NotificationRequest
            {
                NotificationId = DailyReminderId,
                Title = "LexiFlow · 오늘의 복습",
                Description = body,
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = DateTime.Now.Date.AddDays(1).AddHours(9),
                    RepeatType = NotificationRepeat.Daily
                }
            };

            await LocalNotificationCenter.Current.Show(request);
        }
        catch
        {
            // Notifications are an enhancement and must never block learning.
        }
    }
}
