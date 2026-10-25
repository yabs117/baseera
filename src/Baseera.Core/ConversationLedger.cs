namespace Baseera.Core;

public sealed record ConversationExchange(string User, string Assistant, string Attachment, DateTimeOffset Time, bool ContextAllowed = true);

public sealed class ConversationLedger
{
    public const int RetainedExchanges = 20, ContextCharacters = 24000, ContextExchanges = 8, CheckpointInterval = 10;
    private readonly List<ConversationExchange> exchanges = [];
    public IReadOnlyList<ConversationExchange> Exchanges => exchanges.AsReadOnly();
    public long Requests { get; private set; }
    public long Completed { get; private set; }
    private long approvedThrough = CheckpointInterval;
    public bool NeedsConfirmation => Requests >= approvedThrough;
    public string Recipient { get; private set; } = "";
    public bool CanUse(string recipient) => Recipient.Length == 0 || Recipient == recipient;
    public void Bind(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient) || !CanUse(recipient)) throw new InvalidOperationException("A different recipient requires a new conversation.");
        Recipient = recipient;
    }
    public void BeginRequest()
    {
        if (NeedsConfirmation) throw new InvalidOperationException("Confirm the next request batch first.");
        Requests = checked(Requests + 1); // Count attempted requests: failures may still consume service usage.
    }
    public void Continue() => approvedThrough = checked(Requests + CheckpointInterval);
    public void Add(string user, string assistant, string attachment = "", bool contextAllowed = true)
    {
        if (user.Length > 12000 || assistant.Length > 16000 || attachment.Length > 500) throw new ArgumentException("Conversation entry exceeds its bounds.");
        exchanges.Add(new(user, assistant, attachment, DateTimeOffset.UtcNow, contextAllowed)); Completed = checked(Completed + 1);
        if (exchanges.Count > RetainedExchanges) exchanges.RemoveAt(0);
    }
    public IReadOnlyList<ConversationExchange> Context()
    {
        var selected = new List<ConversationExchange>(); int size = 0;
        foreach (var entry in exchanges.Where(x => x.ContextAllowed).Reverse().Take(ContextExchanges))
        {
            int length = entry.User.Length + entry.Assistant.Length + entry.Attachment.Length;
            if (size + length > ContextCharacters) break;
            size += length; selected.Add(entry);
        }
        selected.Reverse(); return selected.AsReadOnly();
    }
    public void Clear() { exchanges.Clear(); Requests = Completed = 0; approvedThrough = CheckpointInterval; Recipient = ""; }
}
