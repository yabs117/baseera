namespace Baseera.Core;

public sealed record WorkItem(Guid Id, string Kind, string State, double? Progress, bool CanCancel, DateTimeOffset Started, DateTimeOffset? Finished);
public sealed class TaskHub
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, WorkLease> active = [];
    private readonly List<WorkItem> finished = [];
    public event Action? Changed;
    public IReadOnlyList<WorkItem> Items { get { lock (gate) return active.Values.Select(x => x.Item).Concat(finished.AsEnumerable().Reverse()).ToArray(); } }
    public WorkLease Begin(string kind, bool canCancel, CancellationToken parent = default)
    {
        var lease = new WorkLease(this, kind, canCancel, parent);
        lock (gate) { if (active.Count >= 16) { lease.DisposeSource(); throw new InvalidOperationException("Too many active operations."); } active.Add(lease.Item.Id, lease); }
        Notify(); return lease;
    }
    public bool Cancel(Guid id)
    {
        WorkLease? lease; lock (gate) { if (!active.TryGetValue(id, out lease) || !lease.Item.CanCancel) return false; }
        lease.Cancel(); Notify(); return true;
    }
    internal void Update(WorkLease lease, string? state = null, double? progress = null)
    {
        lock (gate)
        {
            if (!active.ContainsKey(lease.Item.Id)) return;
            if (state is not null)
            {
                lease.Item = lease.Item with { State = state, CanCancel = false, Finished = DateTimeOffset.UtcNow };
                active.Remove(lease.Item.Id); if (finished.Count == 32) finished.RemoveAt(0); finished.Add(lease.Item);
            }
            else lease.Item = lease.Item with { Progress = progress is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : null };
        }
        Notify();
    }
    private void Notify()
    {
        // A display subscriber must not abort work or leave a native operation untracked.
        if (Changed is { } callbacks) foreach (Action callback in callbacks.GetInvocationList())
            try { callback(); } catch (Exception) { }
    }
}
public sealed class WorkLease : IDisposable
{
    private readonly TaskHub hub;
    private readonly CancellationTokenSource cancellation;
    private int disposed;
    public WorkItem Item { get; internal set; }
    public CancellationToken Token => cancellation.Token;
    internal WorkLease(TaskHub hub, string kind, bool cancel, CancellationToken parent)
    { this.hub = hub; cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent); Item = new(Guid.NewGuid(), kind, "running", null, cancel, DateTimeOffset.UtcNow, null); }
    public void Report(double? value) => hub.Update(this, progress: value);
    public void Complete() => hub.Update(this, "completed");
    public void Failed() => hub.Update(this, "failed");
    public void Cancelled() => hub.Update(this, "canceled");
    internal void Cancel() { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    internal void DisposeSource() => cancellation.Dispose();
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; hub.Update(this, Token.IsCancellationRequested ? "canceled" : "interrupted"); cancellation.Dispose(); }
}
