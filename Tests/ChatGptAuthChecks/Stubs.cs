// No MAUI storage or real application session is touched by this console suite.
namespace LexiFlow.Services
{
    public sealed class SessionService
    {
        public int? CurrentAccountId { get; private set; } = 11;
        public string StorageId => CurrentAccountId?.ToString() ?? "guest";
        public bool IsLoggedIn { get; set; } = true;
        private string _token = "fake-lexiflow-session-one";
        public string? AccessToken => IsLoggedIn ? _token : null;
        public event EventHandler? StateChanged;
        public void Switch(int id)
        {
            CurrentAccountId = id;
            _token = "fake-lexiflow-session-" + id;
            IsLoggedIn = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Microsoft.Maui.Storage
{
    internal sealed class SecureStorage
    {
        public static SecureStorage Default => throw new InvalidOperationException("Native storage is forbidden in tests.");
        public Task<string?> GetAsync(string key) => throw new InvalidOperationException();
        public Task SetAsync(string key, string value) => throw new InvalidOperationException();
        public bool Remove(string key) => throw new InvalidOperationException();
    }
}
