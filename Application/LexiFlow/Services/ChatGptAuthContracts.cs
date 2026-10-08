namespace LexiFlow.Services;

public enum ChatGptFailureKind
{
    NotConnected, PlanNotEnabled, UsageLimit, Unsupported, Unavailable,
    InvalidResponse, SessionChanged, SignInCancelled, StorageUnavailable
}

// Never attach provider exceptions or response bodies: they can contain credentials.
public sealed class ChatGptException : Exception
{
    public ChatGptFailureKind Kind { get; }
    public string SafeMessage => Message;
    public ChatGptException(ChatGptFailureKind kind) : base(MessageFor(kind)) => Kind = kind;
    public ChatGptException(ChatGptFailureKind kind, string safeMessage) : base(safeMessage) => Kind = kind;
    private static string MessageFor(ChatGptFailureKind kind) => kind switch
    {
        ChatGptFailureKind.NotConnected => "ChatGPT 계정을 먼저 연결해 주세요.",
        ChatGptFailureKind.PlanNotEnabled => "이 연결에 ChatGPT 플랜 사용 권한이 없습니다. 다시 연결해 권한을 확인해 주세요.",
        ChatGptFailureKind.UsageLimit => "ChatGPT 사용 한도에 도달했습니다. 잠시 후 다시 시도해 주세요.",
        ChatGptFailureKind.Unsupported => "ChatGPT 연결은 현재 Windows에서만 지원합니다.",
        ChatGptFailureKind.InvalidResponse => "ChatGPT 응답을 안전하게 확인하지 못했습니다. 다시 시도해 주세요.",
        ChatGptFailureKind.SessionChanged => "계정이 변경되었거나 로그인이 만료되었습니다. 다시 시도해 주세요.",
        ChatGptFailureKind.SignInCancelled => "ChatGPT 연결이 취소되었거나 시간이 초과되었습니다.",
        ChatGptFailureKind.StorageUnavailable => "Windows의 보호 저장소에 연결 정보를 저장하지 못했습니다.",
        _ => "ChatGPT에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요."
    };
}

public interface IChatGptTokenProvider
{
    long ConnectionVersion { get; }
    Task<ChatGptAccessGrant> GetGrantAsync(CancellationToken cancellationToken = default);
}

// Deliberately not a record: generated ToString must never print a token.
public sealed class ChatGptAccessGrant
{
    public string AccessToken { get; }
    public string Subject { get; }
    public string ClientId { get; }
    public string OwnerKey { get; }
    public long Version { get; }
    public ChatGptAccessGrant(string accessToken, string subject, string clientId, string ownerKey, long version)
        => (AccessToken, Subject, ClientId, OwnerKey, Version) = (accessToken, subject, clientId, ownerKey, version);
    public override string ToString() => "ChatGptAccessGrant [credentials redacted]";
}

public sealed class ChatGptAccountProfile
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsActive { get; init; }
}
