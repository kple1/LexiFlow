using System.Threading.Channels;

namespace WordApp.Services;

// Queue only intent, never passwords, sessions or raw reset tokens. Bounded memory;
// lost requests on restart can be re-requested. There is no plaintext token outbox.
public sealed record AccountMailRequest(string Purpose, string Email, int? UserId = null, string? SecurityStamp = null);

public sealed class AccountEmailQueue
{
    private readonly Channel<AccountMailRequest> _channel = Channel.CreateBounded<AccountMailRequest>(
        new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public bool Enqueue(AccountMailRequest request) => _channel.Writer.TryWrite(request);
    public bool TryRead(out AccountMailRequest? request) => _channel.Reader.TryRead(out request);
    public IAsyncEnumerable<AccountMailRequest> ReadAllAsync(CancellationToken cancellation) => _channel.Reader.ReadAllAsync(cancellation);
}

public sealed class AccountEmailWorker(AccountEmailQueue queue, IServiceScopeFactory scopes,
    ILogger<AccountEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try { await scope.ServiceProvider.GetRequiredService<AccountEmailWorkflow>().ProcessAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // SMTP/DB exceptions may contain recipient addresses or tokens.
                logger.LogWarning("Account email processing failed ({ErrorType}); request can be retried.", error.GetType().Name);
            }
        }
    }
}
