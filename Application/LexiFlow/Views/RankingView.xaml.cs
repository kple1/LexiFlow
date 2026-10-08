using System.Net;
using System.Diagnostics;
using System.Text.Json;
using LexiFlow.Models;
using LexiFlow.Services;

namespace LexiFlow.Views;

public partial class RankingView : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;
    private CancellationTokenSource? _request;
    private int _generation;
    private bool _active;
    private IDispatcherTimer? _chromaTimer;
    private readonly Stopwatch _chromaClock = new();

    private void SetChromaMotion(bool enabled)
    {
        if (!enabled)
        {
            _chromaTimer?.Stop();
            _chromaClock.Reset();
            return;
        }
        if (_chromaTimer is null)
        {
            _chromaTimer = Dispatcher.CreateTimer();
            _chromaTimer.Interval = TimeSpan.FromMilliseconds(100);
            _chromaTimer.Tick += (_, _) =>
            {
                if (!_active) { SetChromaMotion(false); return; }
                var brush = (LinearGradientBrush)Resources["ChampionStroke"];
                var phase = _chromaClock.Elapsed.TotalSeconds * Math.Tau / 8;
                var x = 0.5 * Math.Cos(phase);
                var y = 0.5 * Math.Sin(phase);
                brush.StartPoint = new Point(0.5 - x, 0.5 - y);
                brush.EndPoint = new Point(0.5 + x, 0.5 + y);
            };
        }
        _chromaClock.Restart();
        _chromaTimer.Start();
    }

    public RankingView(ApiService api, SessionService session)
    {
        InitializeComponent();
        _api = api;
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _active = true;
        _session.StateChanged += OnSessionChanged;
        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _active = false;
        SetChromaMotion(false);
        _session.StateChanged -= OnSessionChanged;
        CancelRequest();
        base.OnDisappearing();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_active) _ = LoadAsync();
    });

    private void CancelRequest()
    {
        ++_generation;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
    }

    private void Clear()
    {
        SetChromaMotion(false);
        rankingList.ItemsSource = null;
        ownCard.IsVisible = false;
        updatedLabel.Text = "";
        emptyLabel.Text = "불러온 순위가 없습니다.";
    }

    private void Busy(bool value)
    {
        spinner.IsVisible = spinner.IsRunning = value;
        refreshButton.IsEnabled = !value;
    }

    private bool Current(int generation, string owner, string? token) => _active && generation == _generation
        && _session.IsLoggedIn && owner == _session.StorageId && token == _session.AccessToken;

    private async Task LoadAsync()
    {
        CancelRequest();
        var generation = _generation;
        var owner = _session.StorageId;
        var token = _session.AccessToken;
        Clear();
        loginButton.IsVisible = !_session.IsLoggedIn;
        if (!_session.IsLoggedIn)
        {
            Busy(false);
            statusLabel.Text = "로그인하면 순위와 내 학습 기록을 확인할 수 있습니다.";
            return;
        }
        _request = new();
        var cancellation = _request.Token;
        Busy(true);
        statusLabel.Text = "학습 기록 불러오는 중…";
        try
        {
            var snapshot = await _api.GetRankingAsync(cancellation);
            if (!Current(generation, owner, token)) return;
            rankingList.ItemsSource = snapshot.Entries;
            SetChromaMotion(snapshot.Entries.Any(row => row.IsChampion));
            ownCard.IsVisible = true;
            ownScoreLabel.Text = $"{snapshot.Me.Score:N0}개 완료";
            ownDetailsLabel.Text = $"단어 {snapshot.Me.MasteredWords:N0} · 문법 {snapshot.Me.MasteredGrammar:N0} · 표현 {snapshot.Me.MasteredIdioms:N0}";
            if (!snapshot.Me.Participating) throw new JsonException("Automatic ranking is not enabled.");
            ownRankLabel.Text = $"{snapshot.Me.Rank:N0}위 · {snapshot.Me.Nickname}";
            updatedLabel.Text = $"전체 {snapshot.ParticipantCount:N0}명 · {snapshot.GeneratedAt.ToLocalTime():MM.dd HH:mm} 기준";
            emptyLabel.Text = "표시할 순위가 없습니다.";
            statusLabel.Text = "학습 후 새로고침하면 서버에 저장된 기록이 반영됩니다.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!Current(generation, owner, token)) return;
            Clear();
            statusLabel.Text = ex switch
            {
                HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "서버 랭킹 업데이트 준비 중입니다. 운영 서버 반영 후 이용할 수 있습니다.",
                HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "요청이 많습니다. 잠시 후 새로고침해 주세요.",
                JsonException => "서버 응답을 확인할 수 없습니다. 다시 불러와 주세요.",
                _ => "연결을 확인하고 다시 불러와 주세요."
            };
        }
        finally
        {
            if (_active && generation == _generation) Busy(false);
        }
    }

    private async void OnRefresh(object? sender, EventArgs e) => await LoadAsync();
    private async void OnLogin(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//account");
}
