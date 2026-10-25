using Baseera.Core;

internal static class ConversationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var thread = new ConversationLedger(); thread.Bind("provider|account-a");
        for (int i = 0; i < 10; i++) thread.BeginRequest();
        bool blocked = false; try { thread.BeginRequest(); } catch (InvalidOperationException) { blocked = true; }
        check(thread.Requests == 10 && thread.NeedsConfirmation && blocked, "Conversation blocks request eleven until explicit continuation, including unsuccessful attempts");
        thread.Continue(); thread.BeginRequest(); check(thread.Requests == 11 && !thread.NeedsConfirmation, "Continuation grants only another request batch without creating an automatic request");
        blocked = false; try { thread.Bind("provider|account-b"); } catch (InvalidOperationException) { blocked = true; }
        check(blocked && !thread.CanUse("other-provider"), "Conversation context cannot migrate to a different recipient without clearing");
        thread.Add("private diagnostic request", "private diagnostic answer", contextAllowed: false);
        check(thread.Exchanges.Count == 1 && thread.Context().Count == 0, "Answers based on one-request diagnostic consent are excluded from subsequent context");
        for (int i = 0; i < 50; i++) thread.Add("user " + i, "answer " + i);
        check(thread.Completed == 51 && thread.Exchanges.Count == 20 && thread.Context().Count == 8 && thread.Context()[0].User == "user 42" && thread.Context()[^1].User == "user 49", "Chat continues with bounded retained history and chronological recent context");
        thread.Clear(); thread.Add(new string('u', 12000), new string('a', 12000)); thread.Add("new", "reply");
        check(thread.Context().Sum(x => x.User.Length + x.Assistant.Length + x.Attachment.Length) <= 24000 && thread.Context()[^1].User == "new", "Context character budget retains recent complete exchanges without unlimited token growth");
        thread.Clear(); check(thread.Requests == 0 && thread.Completed == 0 && thread.Recipient.Length == 0 && !thread.NeedsConfirmation && thread.Exchanges.Count == 0, "New conversation clears recipient, context and its request checkpoint");
    }
}
