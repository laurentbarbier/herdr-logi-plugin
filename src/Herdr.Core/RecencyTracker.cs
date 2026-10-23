namespace Herdr;

// herdr's snapshot carries no activity timestamps, so recency is tracked here:
// a pane moves to the front when its status changes. Merely focusing a pane is
// not activity — tiles would reorder under the press that focused them.
public sealed class RecencyTracker
{
    private sealed class Entry(string status, int seq)
    {
        public string Status = status;
        public int Seq = seq;
    }

    private readonly Dictionary<string, Entry> seen = [];
    private int activitySeq;
    private int firstSeenSeq;
    // Panes present in the first snapshot keep their stable workspace order (they
    // get descending sequence numbers); panes appearing later count as activity.
    private bool bootstrapped;

    public IReadOnlyList<Agent> Rank(IReadOnlyList<Agent> agents)
    {
        lock (seen)
        {
            foreach (var agent in agents)
            {
                if (!seen.TryGetValue(agent.PaneId, out var entry))
                {
                    seen[agent.PaneId] = new Entry(agent.AgentStatus, bootstrapped ? ++activitySeq : --firstSeenSeq);
                }
                else if (entry.Status != agent.AgentStatus)
                {
                    entry.Status = agent.AgentStatus;
                    entry.Seq = ++activitySeq;
                }
            }
            var live = agents.Select(a => a.PaneId).ToHashSet();
            foreach (var paneId in seen.Keys.Where(id => !live.Contains(id)).ToList()) seen.Remove(paneId);
            bootstrapped = true;
            // OrderByDescending is stable, so equal sequence numbers keep their order.
            return agents.OrderByDescending(a => seen.TryGetValue(a.PaneId, out var e) ? e.Seq : 0).ToList();
        }
    }
}
