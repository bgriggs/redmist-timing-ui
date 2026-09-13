using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading;

namespace RedMist.Timing.UI.Clients;

/// <summary>
/// Retries a lost hub connection as the policy it wraps says to, and reports once when a reconnect
/// has gone on long enough to be an outage rather than a dropout.
/// </summary>
/// <remarks>
/// SignalR retries a lost connection itself, and once a connection had been established, the only
/// errors a lasting hub outage ever produced were SignalR's own: the hub connection saying it is
/// reconnecting, and the connection saying each attempt failed, one every few seconds. Nearly all of
/// those are dropouts over in seconds, so crash reporting keeps them as breadcrumbs now, and this is
/// what still says so when the hub stays gone.
///
/// SignalR asks the policy before every attempt and passes how long it has been reconnecting, so the
/// report needs no timer of its own. SignalR measures that time from when the reconnect began, so a
/// phone suspended part way through one comes back with the suspension counted in - and if the first
/// attempt after resuming fails, reports a reconnect the viewer never sat through. Once per reconnect
/// is the whole cost of that, and a hub that cannot be reached right after a resume still could not be
/// reached.
/// </remarks>
internal sealed class ReportingRetryPolicy(IRetryPolicy inner, TimeSpan reportAfter, Action<RetryContext> report) : IRetryPolicy
{
    /// <summary>1 once the current reconnect has been reported. Cleared when SignalR starts another.</summary>
    private int reported;

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // SignalR asks with a count of zero at the start of each reconnect, and at no other time.
        if (retryContext.PreviousRetryCount == 0)
        {
            Volatile.Write(ref reported, 0);
        }
        else if (retryContext.ElapsedTime >= reportAfter && Interlocked.Exchange(ref reported, 1) == 0)
        {
            try
            {
                report(retryContext);
            }
            catch
            {
                // SignalR's reconnect loop is the caller. A report that failed must not end the retries.
            }
        }

        return inner.NextRetryDelay(retryContext);
    }
}
