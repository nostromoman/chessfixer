using System.Threading.Channels;

namespace ChessFixer.Server.Services.Reports;

/// <summary>Tournaments whose report must be written. Processed one by one by <see cref="ReportWorker"/>.</summary>
public class ReportQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(int tournamentId) => _channel.Writer.TryWrite(tournamentId);

    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
