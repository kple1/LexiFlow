namespace LexiFlow.Services;

public sealed class ChatGptConnectionService : IChatGptTokenProvider
{
    private readonly SessionService _session;
    private readonly ChatGptProtectedStore _store;
    private readonly ChatGptOAuthClient _oauth;
    private readonly Func<bool> _supported;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private CancellationTokenSource? _connecting;
    private OwnerSnapshot? _memoryOwner;
    private ChatGptProfileRecord? _active;
    private IReadOnlyList<ChatGptAccountProfile> _profiles = [];
    private readonly HashSet<string> _blockedOwners = new(StringComparer.Ordinal);
    private long _version;

    public ChatGptConnectionService(SessionService session)
        : this(session, new ChatGptSecureStore(), ChatGptOAuthClient.CreateHttpClient(), OperatingSystem.IsWindows) { }

    internal ChatGptConnectionService(SessionService session, IChatGptSecureStore storage, HttpClient http, Func<bool> supported)
    {
        _session = session;
        _store = new ChatGptProtectedStore(storage);
        _oauth = new ChatGptOAuthClient(http);
        _supported = supported;
        _session.StateChanged += OnSessionChanged;
    }

    public bool IsConnected { get { ExpireMemoryIfNeeded(); lock (_stateLock) return _active?.Credentials is not null; } }
    public bool PlanUsageEnabled { get { ExpireMemoryIfNeeded(); lock (_stateLock) return HasPlan(_active?.Credentials); } }
    public string? AccountLabel { get { ExpireMemoryIfNeeded(); lock (_stateLock) return _active is null ? null : Label(_active); } }
    public long ConnectionVersion { get { ExpireMemoryIfNeeded(); return Interlocked.Read(ref _version); } }
    public IReadOnlyList<ChatGptAccountProfile> Profiles { get { ExpireMemoryIfNeeded(); lock (_stateLock) return _profiles; } }
    public event EventHandler? StateChanged;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var owner = CaptureOwner();
        CheckNotBlocked(owner.Key);
        await ChatGptProcessLock.RunAsync(async () =>
        {
            CheckOwner(owner);
            var record = await ReadOwnerAsync(owner.Key);
            CheckOwner(owner);
            Publish(owner, record);
            return true;
        }, cancellationToken);
    }

    public async Task ConnectAsync(Func<Uri, Task> openBrowser, bool newAccount = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);
        var owner = CaptureOwner();
        if (!await _connectGate.WaitAsync(0, cancellationToken)) throw new ChatGptException(ChatGptFailureKind.Unavailable);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        lock (_stateLock) _connecting = lifetime;
        try
        {
            var pending = await ChatGptProcessLock.RunAsync(async () =>
            {
                CheckOwner(owner);
                var record = await ReadOwnerAsync(owner.Key);
                var selected = newAccount ? null : record.Profiles.FirstOrDefault(p => p.Id == record.PendingProfileId)
                    ?? record.Profiles.FirstOrDefault(p => p.Id == record.ActiveProfileId);
                if (selected is null && record.Profiles.Count >= 8)
                    throw new ChatGptException(ChatGptFailureKind.Unavailable, "저장된 ChatGPT 연결이 8개입니다. 기존 연결을 선택해 주세요.");
                var host = await _store.ReadAsync<ChatGptHostRecord>("host");
                if (host is null)
                {
                    host = new ChatGptHostRecord { HostId = "urn:uuid:" + Guid.NewGuid() };
                    CheckOwner(owner);
                    await _store.WriteAsync("host", host);
                }
                if (!host.HostId.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(host.HostId[9..], out _))
                    throw new ChatGptException(ChatGptFailureKind.StorageUnavailable);
                return (Profile: selected, Host: host.HostId);
            }, lifetime.Token);
            CheckOwner(owner);
            using var callback = new ChatGptLoopbackCallback();
            var state = ChatGptOAuthClient.RandomValue();
            var nonce = ChatGptOAuthClient.RandomValue();
            var verifier = ChatGptOAuthClient.RandomValue();
            await openBrowser(ChatGptOAuthClient.AuthorizationUri(pending.Profile?.ClientId, pending.Host, callback.RedirectUri, state, nonce, verifier));
            CheckOwner(owner);
            var returned = await callback.ReceiveAsync(state, pending.Profile?.ClientId, lifetime.Token);
            CheckOwner(owner);
            await ChatGptProcessLock.RunAsync(async () =>
            {
                CheckOwner(owner);
                var record = await ReadOwnerAsync(owner.Key);
                var selected = pending.Profile is null ? null : record.Profiles.FirstOrDefault(p => p.Id == pending.Profile.Id);
                if (pending.Profile is not null && (selected is null || selected.ClientId != returned.ClientId))
                    throw new ChatGptException(ChatGptFailureKind.SessionChanged);
                if (selected is null)
                {
                    if (record.Profiles.Count >= 8 || record.Profiles.Any(p => p.ClientId == returned.ClientId))
                        throw new ChatGptException(ChatGptFailureKind.InvalidResponse);
                    selected = new ChatGptProfileRecord { ClientId = returned.ClientId };
                    record.Profiles.Add(selected);
                    record.PendingProfileId = selected.Id;
                    // Keep this issued registration even if the one-time code fails.
                    await _store.WriteAsync(OwnerKey(owner.Key), record);
                }
                CheckOwner(owner);
                var credentials = await _oauth.ExchangeAsync(selected.ClientId, returned.Code, verifier, callback.RedirectUri, lifetime.Token);
                var identity = await _oauth.ValidateIdentityAsync(credentials.IdToken, selected.ClientId, nonce, selected.Subject, lifetime.Token);
                CheckOwner(owner);
                lifetime.Token.ThrowIfCancellationRequested();
                var previousSubject = selected.Subject;
                var previousEmail = selected.Email;
                var previousCredentials = selected.Credentials;
                var previousActive = record.ActiveProfileId;
                var previousPending = record.PendingProfileId;
                selected.Subject = identity.Subject;
                selected.Email = identity.Email;
                selected.Credentials = credentials;
                record.ActiveProfileId = selected.Id;
                record.PendingProfileId = null;
                lifetime.Token.ThrowIfCancellationRequested();
                await _store.WriteAsync(OwnerKey(owner.Key), record);
                try
                {
                    CheckOwner(owner);
                    lifetime.Token.ThrowIfCancellationRequested();
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ChatGptException { Kind: ChatGptFailureKind.SessionChanged })
                {
                    // Cancellation during the non-cancellable secure-store commit must
                    // not silently activate this account on the next application launch.
                    selected.Subject = previousSubject;
                    selected.Email = previousEmail;
                    selected.Credentials = previousCredentials;
                    record.ActiveProfileId = previousActive;
                    record.PendingProfileId = previousPending;
                    try { await _store.WriteAsync(OwnerKey(owner.Key), record); }
                    catch { lock (_stateLock) _blockedOwners.Add(owner.Key); }
                    throw;
                }
                lock (_stateLock) _blockedOwners.Remove(owner.Key);
                Publish(owner, record, forceVersion: true);
                return true;
            }, lifetime.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!MatchesSession(owner)) throw new ChatGptException(ChatGptFailureKind.SessionChanged);
            throw new ChatGptException(ChatGptFailureKind.SignInCancelled);
        }
        catch (ChatGptException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch { throw new ChatGptException(ChatGptFailureKind.Unavailable); }
        finally
        {
            lock (_stateLock) if (ReferenceEquals(_connecting, lifetime)) _connecting = null;
            _connectGate.Release();
        }
    }

    public async Task<ChatGptAccessGrant> GetGrantAsync(CancellationToken cancellationToken = default)
    {
        var owner = CaptureOwner();
        CheckNotBlocked(owner.Key);
        return await ChatGptProcessLock.RunAsync(async () =>
        {
            CheckOwner(owner);
            CheckNotBlocked(owner.Key);
            // Always reread after taking the cross-process lock: refresh tokens rotate.
            var record = await ReadOwnerAsync(owner.Key);
            var selected = record.Profiles.FirstOrDefault(p => p.Id == record.ActiveProfileId);
            if (selected?.Credentials is not { } credentials || selected.Subject is null)
            {
                Publish(owner, record);
                throw new ChatGptException(ChatGptFailureKind.NotConnected);
            }
            if (!HasPlan(credentials)) throw new ChatGptException(ChatGptFailureKind.PlanNotEnabled);
            if (credentials.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(90))
            {
                if (credentials.EarliestRefreshAt > DateTimeOffset.UtcNow)
                {
                    if (credentials.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30)) throw new ChatGptException(ChatGptFailureKind.Unavailable);
                }
                else
                {
                    try
                    {
                        var refreshed = await _oauth.RefreshAsync(selected.ClientId, credentials.RefreshToken, cancellationToken);
                        var identity = await _oauth.ValidateIdentityAsync(refreshed.IdToken, selected.ClientId, null, selected.Subject, cancellationToken);
                        CheckOwner(owner);
                        selected.Credentials = refreshed;
                        selected.Email = identity.Email ?? selected.Email;
                        try { await _store.WriteAsync(OwnerKey(owner.Key), record); }
                        catch (ChatGptException)
                        {
                            // The provider has already rotated the refresh token. Do not
                            // fall back to the previous token set after a local commit failure.
                            lock (_stateLock) _blockedOwners.Add(owner.Key);
                            InvalidateMemory();
                            throw;
                        }
                        credentials = refreshed;
                    }
                    catch (ChatGptException ex) when (ex.Kind == ChatGptFailureKind.NotConnected)
                    {
                        CheckOwner(owner);
                        selected.Credentials = null;
                        await _store.WriteAsync(OwnerKey(owner.Key), record);
                        Publish(owner, record);
                        throw;
                    }
                }
            }
            CheckOwner(owner);
            Publish(owner, record);
            if (!HasPlan(credentials)) throw new ChatGptException(ChatGptFailureKind.PlanNotEnabled);
            return new ChatGptAccessGrant(credentials.AccessToken, selected.Subject, selected.ClientId, owner.Key, Interlocked.Read(ref _version));
        }, cancellationToken);
    }

    public async Task SelectProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var owner = CaptureOwner();
        await ChatGptProcessLock.RunAsync(async () =>
        {
            CheckOwner(owner);
            var record = await ReadOwnerAsync(owner.Key);
            if (!record.Profiles.Any(p => p.Id == profileId)) throw new ChatGptException(ChatGptFailureKind.NotConnected);
            record.ActiveProfileId = profileId;
            record.PendingProfileId = null;
            await _store.WriteAsync(OwnerKey(owner.Key), record);
            CheckOwner(owner);
            Publish(owner, record, forceVersion: true);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        var ownerKey = _session.CurrentAccountId?.ToString();
        if (ownerKey is not null) lock (_stateLock) _blockedOwners.Add(ownerKey);
        InvalidateMemory();
        if (ownerKey is null) return true;
        // Cleanup remains bound to the explicitly captured owner, even after sign-out.
        return await ChatGptProcessLock.RunAsync(async () =>
        {
            var record = await ReadOwnerAsync(ownerKey);
            var selected = record.Profiles.FirstOrDefault(p => p.Id == record.ActiveProfileId);
            if (selected?.Credentials is not { } credentials)
            {
                RestoreDisconnectedProfiles(ownerKey, record);
                return true;
            }
            var confirmed = false;
            try { confirmed = await _oauth.RevokeAsync(selected.ClientId, credentials.RefreshToken, cancellationToken); }
            finally
            {
                selected.Credentials = null;
                await _store.WriteAsync(OwnerKey(ownerKey), record);
                RestoreDisconnectedProfiles(ownerKey, record);
            }
            return confirmed;
        }, CancellationToken.None);
    }

    public async Task ForgetCurrentOwnerAsync(CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        var ownerKey = _session.CurrentAccountId?.ToString();
        if (ownerKey is not null) lock (_stateLock) _blockedOwners.Add(ownerKey);
        InvalidateMemory();
        if (ownerKey is null) return;
        await ChatGptProcessLock.RunAsync(async () =>
        {
            // Account deletion must clear every registration, not just the active one.
            // Remote sessions can also be disconnected in ChatGPT Settings.
            await _store.WriteAsync(OwnerKey(ownerKey), new ChatGptOwnerRecord { OwnerKey = ownerKey });
            return true;
        }, CancellationToken.None);
    }

    private async Task<ChatGptOwnerRecord> ReadOwnerAsync(string key)
    {
        var value = await _store.ReadAsync<ChatGptOwnerRecord>(OwnerKey(key)) ?? new ChatGptOwnerRecord { OwnerKey = key };
        if (value.OwnerKey != key || value.Profiles is null || value.Profiles.Count > 8
            || value.Profiles.Any(p => p is null || !Guid.TryParseExact(p.Id, "N", out _) || !ChatGptOAuthClient.IsIssuedClientId(p.ClientId)
                || p.Subject is { Length: > 256 } || p.Email is { Length: > 254 }
                || (p.Credentials is { } c && (string.IsNullOrWhiteSpace(p.Subject)
                    || !ValidSecret(c.AccessToken) || !ValidSecret(c.RefreshToken) || !ValidSecret(c.IdToken)
                    || c.Scope is null || c.Scope.Length > 4096 || c.SavedAt > DateTimeOffset.UtcNow.AddMinutes(1)
                    || c.ExpiresAt > c.SavedAt.AddHours(2))))
            || value.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != value.Profiles.Count
            || value.Profiles.Select(p => p.ClientId).Distinct(StringComparer.Ordinal).Count() != value.Profiles.Count
            || (value.ActiveProfileId is not null && !value.Profiles.Any(p => p.Id == value.ActiveProfileId))
            || (value.PendingProfileId is not null && !value.Profiles.Any(p => p.Id == value.PendingProfileId)))
            throw new ChatGptException(ChatGptFailureKind.StorageUnavailable);
        return value;
    }

    private void Publish(OwnerSnapshot owner, ChatGptOwnerRecord record, bool forceVersion = false)
    {
        CheckOwner(owner);
        lock (_stateLock)
        {
            CheckOwner(owner);
            var active = record.Profiles.FirstOrDefault(p => p.Id == record.ActiveProfileId);
            var changed = forceVersion || _memoryOwner?.Key != owner.Key || _active?.Id != active?.Id
                || (_active?.Credentials is null) != (active?.Credentials is null);
            _memoryOwner = owner;
            _active = active;
            _profiles = record.Profiles.Select(p => new ChatGptAccountProfile
            {
                Id = p.Id, Label = Label(p), IsActive = p.Id == record.ActiveProfileId
            }).ToArray();
            if (changed) Interlocked.Increment(ref _version);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private OwnerSnapshot CaptureOwner()
    {
        EnsureSupported();
        ExpireMemoryIfNeeded();
        if (!_session.IsLoggedIn || _session.CurrentAccountId is null || _session.AccessToken is not { } token)
            throw new ChatGptException(ChatGptFailureKind.SessionChanged);
        return new OwnerSnapshot(_session.StorageId, token, Interlocked.Read(ref _version));
    }
    private void CheckOwner(OwnerSnapshot owner)
    {
        if (!MatchesSession(owner) || Interlocked.Read(ref _version) != owner.Version)
            throw new ChatGptException(ChatGptFailureKind.SessionChanged);
    }
    private bool MatchesSession(OwnerSnapshot owner) => _session.IsLoggedIn && _session.StorageId == owner.Key
        && string.Equals(_session.AccessToken, owner.SessionToken, StringComparison.Ordinal);
    private void EnsureSupported() { if (!_supported()) throw new ChatGptException(ChatGptFailureKind.Unsupported); }
    private void RestoreDisconnectedProfiles(string ownerKey, ChatGptOwnerRecord record)
    {
        lock (_stateLock) _blockedOwners.Remove(ownerKey);
        if (_session.IsLoggedIn && _session.StorageId == ownerKey)
        {
            var currentOwner = CaptureOwner();
            Publish(currentOwner, record);
        }
    }
    private void CheckNotBlocked(string key)
    {
        lock (_stateLock) if (_blockedOwners.Contains(key)) throw new ChatGptException(ChatGptFailureKind.NotConnected);
    }
    private void OnSessionChanged(object? sender, EventArgs e) => InvalidateMemory();
    private void ExpireMemoryIfNeeded()
    {
        bool stale;
        lock (_stateLock) stale = _memoryOwner is not null && !MatchesSession(_memoryOwner);
        if (stale) InvalidateMemory();
    }
    private void InvalidateMemory()
    {
        lock (_stateLock)
        {
            _connecting?.Cancel();
            _memoryOwner = null;
            _active = null;
            _profiles = [];
            Interlocked.Increment(ref _version);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private static bool ValidSecret(string? value) => value is { Length: > 0 and <= 32768 } && !value.Any(char.IsWhiteSpace);
    private static bool HasPlan(ChatGptCredentials? value) => value?.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(ChatGptOAuthClient.DirectScope, StringComparer.Ordinal) == true;
    private static string Label(ChatGptProfileRecord p) => $"{p.Email ?? "ChatGPT 계정"} · {p.Id[..6]}";
    private static string OwnerKey(string key) => "owner:" + key;
    private sealed class OwnerSnapshot(string key, string sessionToken, long version)
    {
        public string Key { get; } = key;
        public string SessionToken { get; } = sessionToken;
        public long Version { get; } = version;
    }
}
