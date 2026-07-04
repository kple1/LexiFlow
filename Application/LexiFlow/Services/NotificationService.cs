using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace LexiFlow.Services;

// Schedules a daily "review due" reminder via local notifications. Best-effort:
// any platform/permission issue is swallowed so it never disrupts the app.
public class NotificationService
{
    private const int DailyReminderId = 1001;

    public async Task ScheduleDailyReminderAsync(int dueCount)
    {
        try
        {
            if (!await LocalNotificationCenter.Current.AreNotificationsEnabled())
                await LocalNotificationCenter.Current.RequestNotificationPermission();

            var body = dueCount > 0
                ? $"You have {dueCount} items due for review."
                : "Time for your daily review!";

            var request = new NotificationRequest
            {
                NotificationId = DailyReminderId,
                Title = "LexiFlow",
                Description = body,
                Schedule = new NotificationRequestSchedule
                {
                    // First fire tomorrow morning, then repeat every day.
                    NotifyTime = DateTime.Now.Date.AddDays(1).AddHours(9),
                    RepeatType = NotificationRepeat.Daily
                }
            };

            await LocalNotificationCenter.Current.Show(request);
        }
        catch
        {
            // Best-effort; ignore.
        }
    }
}
