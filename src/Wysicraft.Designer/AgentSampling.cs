using ModelContextProtocol.Server;
using Wysicraft.Core;
namespace Wysicraft.Designer;

// Asking the assistant directly, rather than leaving a note and hoping someone mentions it.
//
// MCP has a call for this — sampling — where the server asks the client's model a question and gets the answer back.
// Two things have to be true: the transport must keep a session (Stateless mode cannot send the client anything it
// did not ask for), and the client must say it supports sampling. The first is now true; the second is up to
// whoever connects, so this tries and falls back to the queue rather than assuming.
//
// The session is borrowed from whichever tool call came last. That is the only handle a server gets on a client
// outside a request, and it may be stale by the time a button is pressed — so every path here treats failure as
// normal and returns false, leaving the request queued exactly as before.
public partial class MainWindow
{
    McpServer? liveSession;
    internal string samplingState = "unknown";

    /// <summary>Remembers the session a tool call arrived on, so a button press later has something to ask through.
    ///
    /// A session that can sample is kept in preference to one that cannot. More than one client can be connected —
    /// an assistant doing the work and a script reading the project, say — and without this rule whichever called
    /// most recently would win, so a plain HTTP client could quietly take away the ability to ask anything.</summary>
    internal void RememberSession(McpServer? server)
    {
        if (server == null) return;
        // ClientCapabilities is what the client sent at initialize; sampling is the one that matters here.
        bool canSample = server.ClientCapabilities?.Sampling != null;
        if (!canSample && CanAskDirectly) return;   // don't let a client that can't ask displace one that can
        liveSession = server;
        samplingState = canSample ? "live" : "queue";
    }

    /// <summary>True when the assistant can be asked directly, so the editor can say so rather than guessing.</summary>
    internal bool CanAskDirectly => liveSession != null && samplingState == "live";

    /// <summary>Why the assistant cannot be asked directly, in words, or "" when it can. Saying only "waiting" left
    /// no way to tell "nothing has connected yet" apart from "this client will never be able to", which are
    /// different problems with different fixes.</summary>
    internal string WhyQueued => liveSession == null
        ? "No assistant has used this server yet, so there is no session to ask through. Have it call any tool — get_project will do — then try again."
        : samplingState == "live" ? ""
        : "The assistant that connected does not offer MCP sampling, so it cannot be asked from in here. It can still read this from the queue.";

    /// <summary>Asks the assistant then and there. Returns its reply, or null if that was not possible — in which
    /// case the caller leaves the request on the queue, which is what happened before this existed.</summary>
    internal async Task<string?> AskDirectlyAsync(string prompt, int maxTokens = 2000)
    {
        var session = liveSession;
        if (session == null) return null;
        try
        {
            var reply = await session.SampleAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt)],
                new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = maxTokens },
                cancellationToken: CancellationToken.None);
            samplingState = "live";
            return reply.Text;
        }
        catch (InvalidOperationException)
        {
            // The documented answer for "this client cannot sample". Not an error, just the other path.
            samplingState = "queue";
            return null;
        }
        catch (Exception ex)
        {
            samplingState = "queue";
            Log("Asking the assistant directly failed, so the request was queued instead: " + ex.Message);
            return null;
        }
    }
}
