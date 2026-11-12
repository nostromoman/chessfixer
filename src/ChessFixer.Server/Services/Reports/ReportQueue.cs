using System.Threading.Channels;

namespace ChessFixer.Server.Services.Reports;

/// <summary>A report to write: of the tournament, or of one of its rounds (<paramref name="RoundId"/>).</summary>
public record ReportJob(int TournamentId, int? RoundId = null);

/// <summary>Reports that must be written. Processed one by one by <see cref="ReportWorker"/>.</summary>
public class ReportQueue
{
    private readonly Channel<ReportJob> _channel = Channel.CreateUnbounded<ReportJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(ReportJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<ReportJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
