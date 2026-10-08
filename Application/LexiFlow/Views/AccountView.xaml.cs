using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class AccountView : ContentPage
{
    private readonly SessionService _session;
    private readonly ApiService _api;
    private readonly StreakService _streak;
    private readonly LearningMetricsService _metrics;
    private readonly NotificationService _notifications;
    private readonly ChatGptConnectionService _chatGpt;
    private bool _securityBusy;
    private bool _chatGptBusy;
    private bool _viewActive;
    private CancellationTokenSource? _chatGptCancellation;

    public AccountView(
        SessionService session,
        ApiService api,
        StreakService streak,
        LearningMetricsService metrics,
        NotificationService notifications,
        ChatGptConnectionService chatGpt)
    {
        InitializeComponent();
        _session = session;
        _api = api;
        _streak = streak;
        _metrics = metrics;
        _notifications = notifications;
        _chatGpt = chatGpt;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewActive = true;
        UpdateLocalMetrics();
        await Task.WhenAll(LoadStatsAsync(), LoadReminderStateAsync(), LoadRecoveryEmailAsync(), LoadChatGptStateAsync());
    }

    protected override void OnDisappearing()
    {
        _viewActive = false;
        _chatGptCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadChatGptStateAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            chatGptStatusLabel.Text = "ChatGPT 연결은 현재 Windows 개인용 앱에서 지원합니다.";
            chatGptActions.IsVisible = false;
            return;
        }
        try
        {
            await _chatGpt.RestoreAsync();
            if (_viewActive) UpdateChatGptState();
        }
        catch (Exception error)
        {
            if (_viewActive) chatGptStatusLabel.Text = ChatGptErrorMessage(error);
        }
    }

    private void UpdateChatGptState()
    {
        chatGptStatusLabel.Text = _chatGpt.PlanUsageEnabled
            ? $"연결됨 · {_chatGpt.AccountLabel ?? "ChatGPT"} · 문장 판별은 GPT-6.1 Sol 고정입니다."
            : _chatGpt.IsConnected
                ? "로그인은 되었지만 플랜 사용 권한이 없습니다. 다시 연결하여 사용량 공유에 동의해 주세요."
                : "아직 연결되지 않았습니다. 연결 전 답안은 OpenAI로 전송하지 않습니다.";
        chatGptConnectButton.Text = "Continue with ChatGPT";
        chatGptOtherAccountButton.IsVisible = _chatGpt.IsConnected;
        chatGptDisconnectButton.IsVisible = _chatGpt.IsConnected;
        var profiles = _chatGpt.Profiles.ToList();
        chatGptProfilesPanel.IsVisible = profiles.Count > 1;
        chatGptProfilePicker.ItemsSource = profiles;
        chatGptProfilePicker.SelectedIndex = profiles.FindIndex(profile => profile.IsActive);
    }

    private async void OnChatGptSelectProfileClick(object? sender, EventArgs e)
    {
        if (_chatGptBusy || _securityBusy || !_session.IsLoggedIn || chatGptProfilePicker.SelectedItem is not ChatGptAccountProfile profile) return;
        SetChatGptBusy(true);
        try
        {
            await _chatGpt.SelectProfileAsync(profile.Id);
            if (_viewActive) UpdateChatGptState();
        }
        catch (Exception error) { if (_viewActive) chatGptStatusLabel.Text = ChatGptErrorMessage(error); }
        finally { SetChatGptBusy(false); }
    }

    private async void OnChatGptConnectClick(object? sender, EventArgs e) => await ConnectChatGptAsync(false);
    private async void OnChatGptOtherAccountClick(object? sender, EventArgs e) => await ConnectChatGptAsync(true);

    private async Task ConnectChatGptAsync(bool newAccount)
    {
        if (_chatGptBusy || _securityBusy || !_session.IsLoggedIn || !OperatingSystem.IsWindows()) return;
        var owner = _session.StorageId;
        var sessionToken = _session.AccessToken;
        using var cancellation = new CancellationTokenSource();
        _chatGptCancellation = cancellation;
        SetChatGptBusy(true);
        chatGptStatusLabel.Text = "브라우저의 OpenAI 화면에서 로그인·동의를 마쳐 주세요. 이 창은 열린 채로 두세요.";
        try
        {
            await _chatGpt.ConnectAsync(async uri =>
            {
                if (!await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred))
                    throw new ChatGptException(ChatGptFailureKind.Unavailable);
            }, newAccount, cancellation.Token);
            if (_viewActive && _session.IsLoggedIn && owner == _session.StorageId && sessionToken == _session.AccessToken)
            {
                UpdateChatGptState();
                if (_chatGpt.PlanUsageEnabled)
                {
                    var welcomeKey = LocalAccountData.Key(_session, "chatgpt_plan_welcome_v1");
                    var shown = false;
                    try { shown = Preferences.Get(welcomeKey, false); } catch { }
                    if (!shown)
                    {
                        await DisplayAlertAsync("ChatGPT 플랜을 사용합니다",
                            "LexiFlow의 AI 문장 판별은 본인의 ChatGPT 플랜 사용량에 포함됩니다. ChatGPT 설정에서 사용량과 연결을 관리할 수 있습니다. 별도 API 키 결제는 사용하지 않습니다.", "알겠어요");
                        if (_session.IsLoggedIn && owner == _session.StorageId && sessionToken == _session.AccessToken)
                            try { Preferences.Set(welcomeKey, true); } catch { }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_viewActive) chatGptStatusLabel.Text = "연결을 취소했거나 시간이 초과되었습니다. 다시 연결할 수 있습니다.";
        }
        catch (Exception error)
        {
            if (_viewActive && owner == _session.StorageId) chatGptStatusLabel.Text = ChatGptErrorMessage(error);
        }
        finally
        {
            if (ReferenceEquals(_chatGptCancellation, cancellation)) _chatGptCancellation = null;
            SetChatGptBusy(false);
        }
    }

    private async void OnChatGptDisconnectClick(object? sender, EventArgs e)
    {
        if (_chatGptBusy || _securityBusy) return;
        if (!await DisplayAlertAsync("ChatGPT 연결 해제", "이 기기의 현재 ChatGPT 연결을 해제할까요? LexiFlow 학습 기록은 유지합니다.", "해제", "취소")) return;
        SetChatGptBusy(true);
        try
        {
            var revoked = await _chatGpt.DisconnectAsync();
            if (!_viewActive) return;
            UpdateChatGptState();
            if (!revoked) chatGptStatusLabel.Text = "기기에서 연결을 해제했습니다. OpenAI 측 권한 폐기는 확인하지 못했으므로 ChatGPT 설정에서도 연결을 해제해 주세요.";
        }
        catch (Exception error)
        {
            if (_viewActive) chatGptStatusLabel.Text = ChatGptErrorMessage(error);
        }
        finally { SetChatGptBusy(false); }
    }

    private void OnChatGptCancelClick(object? sender, EventArgs e) => _chatGptCancellation?.Cancel();

    private async void OnChatGptUsageClick(object? sender, EventArgs e)
    {
        try { await Browser.Default.OpenAsync(new Uri("https://chatgpt.com/#settings/Usage"), BrowserLaunchMode.SystemPreferred); }
        catch { if (_viewActive) await DisplayAlertAsync("브라우저 열기 실패", "ChatGPT 설정에서 Usage와 연결된 앱을 확인해 주세요.", "확인"); }
    }

    private void SetChatGptBusy(bool busy)
    {
        _chatGptBusy = busy;
        chatGptActions.IsEnabled = !busy;
        chatGptActivity.IsVisible = chatGptActivity.IsRunning = busy;
        chatGptCancelButton.IsVisible = busy && _chatGptCancellation is not null;
        securityActions.IsEnabled = !busy && !_securityBusy;
    }

    private static string ChatGptErrorMessage(Exception error) => error is ChatGptException chatGptError
        ? chatGptError.SafeMessage
        : "ChatGPT 연결을 완료하지 못했습니다. 네트워크 또는 기기 보호 저장소 상태를 확인해 주세요.";

    private async Task LoadRecoveryEmailAsync()
    {
        try
        {
            var status = await _api.GetRecoveryEmailAsync();
            recoveryEmailStatusLabel.Text = status?.Email is { } email ? "인증된 복구 이메일: " + email : "인증된 복구 이메일이 없습니다.";
            recoveryEmailButton.IsEnabled = status?.Enabled == true;
            if (status?.Enabled != true) recoveryEmailStatusLabel.Text += " 이메일 기능 준비 중입니다.";
        }
        catch { recoveryEmailStatusLabel.Text = "복구 이메일 상태를 확인하지 못했습니다."; recoveryEmailButton.IsEnabled = false; }
    }

    private async void OnRecoveryEmailClick(object? sender, EventArgs e)
    {
        if (_securityBusy || _chatGptBusy) return;
        if (string.IsNullOrWhiteSpace(recoveryEmailEntry.Text) || string.IsNullOrEmpty(currentPasswordEntry.Text))
        { await DisplayAlertAsync("입력 확인", "이메일과 현재 비밀번호를 입력해 주세요.", "확인"); return; }
        _securityBusy = true;
        securityActions.IsEnabled = false;
        try
        {
            await _api.RequestRecoveryEmailAsync(recoveryEmailEntry.Text.Trim(), currentPasswordEntry.Text);
            await DisplayAlertAsync("인증메일 요청", "연결 가능한 주소라면 메일을 보냅니다. 메일에서 직접 확인을 마치면 모든 기기에서 다시 로그인해야 합니다.", "확인");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { await DisplayAlertAsync("인증 실패", "현재 비밀번호를 확인해 주세요.", "확인"); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        { await DisplayAlertAsync("잠시 기다려 주세요", "요청이 많습니다. 잠시 후 다시 시도해 주세요.", "확인"); }
        catch { await DisplayAlertAsync("메일 요청 실패", "이메일 주소, 연결 상태 또는 서비스 준비 상태를 확인해 주세요.", "확인"); }
        finally { currentPasswordEntry.Text = ""; securityActions.IsEnabled = true; _securityBusy = false; }
    }

    private void UpdateLocalMetrics()
    {
        var user = _session.CurrentUserId ?? "-";
        userLabel.Text = user;
        avatarLabel.Text = user.Length > 0 ? user[..1].ToUpperInvariant() : "L";
        accountLevelLabel.Text = $"LEVEL {_metrics.Level} · {_metrics.TotalXp:N0} XP";
        streakLabel.Text = $"{_streak.Current}일";
        dailyGoalLabel.Text = $"{_metrics.TodayReviews} / {_metrics.DailyGoal}문제";
        dailyGoalProgress.Progress = _metrics.DailyGoalProgress;
        dailyGoalStatusLabel.Text = _metrics.TodayReviews >= _metrics.DailyGoal ? "달성 완료" : "진행 중";
        dailyGoalStatusLabel.TextColor = _metrics.TodayReviews >= _metrics.DailyGoal
            ? ThemeColors.Get("Success")
            : ThemeColors.Get("Primary");
        levelProgressLabel.Text = $"다음 레벨까지 {_metrics.XpToNextLevel} XP";
    }

    private async Task LoadStatsAsync()
    {
        if (!_session.IsLoggedIn)
            return;

        loadingOverlay.IsVisible = true;
        errorBorder.IsVisible = false;

        try
        {
            var userId = _session.CurrentUserId!;
            var wordsTask = _api.GetProgressAsync(userId);
            var grammarTask = _api.GetGrammarProgressAsync(userId);
            var idiomTask = _api.GetIdiomProgressAsync(userId);
            await Task.WhenAll(wordsTask, grammarTask, idiomTask);

            var statuses = (await wordsTask).Select(item => item.Status)
                .Concat((await grammarTask).Select(item => item.Status))
                .Concat((await idiomTask).Select(item => item.Status))
                .ToList();

            masteredLabel.Text = statuses.Count(status => status == "Mastered").ToString();
            learningLabel.Text = statuses.Count(status => status == "Learning").ToString();
            totalLabel.Text = $"총 {statuses.Count}개 학습";
        }
        catch
        {
            errorBorder.IsVisible = true;
        }
        finally
        {
            loadingOverlay.IsVisible = false;
        }
    }

    private async Task LoadReminderStateAsync()
    {
        var enabled = await _notifications.AreNotificationsEnabledAsync();
        reminderButton.Text = enabled ? "켜짐" : "켜기";
        reminderButton.IsEnabled = !enabled;
        reminderStatusLabel.Text = enabled
            ? "내일부터 매일 오전 9시에 알려드릴게요."
            : "알림 권한은 원할 때만 요청해요.";
    }

    private async void OnEnableReminderClick(object? sender, EventArgs e)
    {
        reminderButton.IsEnabled = false;
        reminderButton.Text = "설정 중";

        var enabled = await _notifications.EnableDailyReminderAsync();
        reminderButton.Text = enabled ? "켜짐" : "다시 시도";
        reminderButton.IsEnabled = !enabled;
        reminderStatusLabel.Text = enabled
            ? "내일부터 매일 오전 9시에 알려드릴게요."
            : "알림 권한이 꺼져 있어요. 시스템 설정을 확인해 주세요.";
    }

    private async void OnSignOutClick(object? sender, EventArgs e)
    {
        if (_securityBusy || _chatGptBusy) return;
        var confirm = await DisplayAlertAsync("로그아웃", "이 기기에서 로그아웃할까요?", "로그아웃", "취소");
        if (!confirm) return;
        try { await _api.LogoutAsync(); }
        catch
        {
            await DisplayAlertAsync("기기에서 로그아웃", "서버에 연결하지 못해 서버 세션은 즉시 폐기되지 않았습니다. 세션은 발급 후 최대 7일에 만료됩니다.", "확인");
        }
        finally { _session.SignOut(); }
    }

    private async void OnChangePasswordClick(object? sender, EventArgs e)
    {
        if (_securityBusy || _chatGptBusy) return;
        var current = currentPasswordEntry.Text ?? "";
        var next = newPasswordEntry.Text ?? "";
        if (string.IsNullOrEmpty(current) || next.Length < 12 || System.Text.Encoding.UTF8.GetByteCount(next) > 72)
        {
            await DisplayAlertAsync("입력 확인", "현재 비밀번호와 새 비밀번호를 입력해 주세요. 새 비밀번호는 12자 이상, UTF-8 72바이트 이내여야 합니다.", "확인");
            return;
        }
        if (!await DisplayAlertAsync("비밀번호 변경", "변경 후 모든 기기에서 로그아웃됩니다. 계속할까요?", "변경", "취소")) return;
        _securityBusy = true;
        securityActions.IsEnabled = false;
        try { await _api.ChangePasswordAsync(current, next); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { await DisplayAlertAsync("변경하지 못했어요", "현재 비밀번호를 확인해 주세요.", "확인"); }
        catch { await DisplayAlertAsync("변경하지 못했어요", "연결과 로그인 상태를 확인한 뒤 다시 시도해 주세요.", "확인"); }
        finally
        {
            currentPasswordEntry.Text = newPasswordEntry.Text = "";
            _securityBusy = false;
            securityActions.IsEnabled = true;
        }
    }

    private async void OnDeleteAccountClick(object? sender, EventArgs e)
    {
        if (_securityBusy || _chatGptBusy) return;
        var current = currentPasswordEntry.Text ?? "";
        if (string.IsNullOrEmpty(current))
        {
            await DisplayAlertAsync("비밀번호 확인", "현재 비밀번호를 먼저 입력해 주세요.", "확인");
            return;
        }
        if (!await DisplayAlertAsync("계정을 삭제할까요?",
            "서버 계정·학습 기록과 이 기기의 Archive·코스·XP·저장된 ChatGPT 연결을 삭제합니다. 복구할 수 없습니다. 다른 기기에 남은 로컬 데이터는 그 기기에서 별도로 삭제해야 합니다.",
            "계정 삭제", "취소")) return;
        _securityBusy = true;
        securityActions.IsEnabled = false;
        try
        {
            await _api.DeleteAccountAsync(current);
            try
            {
                try { if (OperatingSystem.IsWindows()) await _chatGpt.ForgetCurrentOwnerAsync(); }
                finally { LocalAccountData.DeleteCurrent(_session); }
            }
            finally { _session.SignOut(); }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        { await DisplayAlertAsync("삭제하지 못했어요", "현재 비밀번호를 확인해 주세요.", "확인"); }
        catch { await DisplayAlertAsync("삭제 상태 확인 필요", "연결이 끊겼거나 일부 기기 데이터를 지우지 못했습니다. 다시 로그인해 계정 상태를 확인해 주세요.", "확인"); }
        finally
        {
            currentPasswordEntry.Text = newPasswordEntry.Text = "";
            _securityBusy = false;
            securityActions.IsEnabled = true;
        }
    }
}
