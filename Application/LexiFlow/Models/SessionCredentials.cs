namespace LexiFlow.Models;

public sealed record SessionCredentials(int Id, string UserId, string AccessToken, DateTimeOffset ExpiresAt);
