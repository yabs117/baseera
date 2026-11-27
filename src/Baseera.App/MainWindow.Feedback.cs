using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private const string WorkerUrl = "https://baseera-feedback.yabsganes.workers.dev/";
    private static readonly HttpClient FeedbackHttp = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private static readonly string[] FeedbackKinds = ["feedbackIdea", "feedbackBug", "feedbackOther"];
    private string feedbackTitleDraft = "", feedbackBodyDraft = "", feedbackStatus = "";
    private int feedbackKindDraft;
    private bool feedbackSending;
    private Action? refreshFeedback;
    internal HttpClient? FeedbackHttpForSmoke { get; set; }

    private void FeedbackPage(StackPanel body)
    {
        Heading(body, "feedback", "feedbackDesc");
        var panel = Stack();
        panel.Children.Add(Text(I18n.T("feedbackKind")));
        var kind = new ComboBox { ItemsSource = FeedbackKinds.Select(I18n.T).ToArray(), SelectedIndex = feedbackKindDraft };
        AutomationProperties.SetName(kind, I18n.T("feedbackKind"));
        DeviceId(kind, "FeedbackKind");
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0) return;
            feedbackKindDraft = kind.SelectedIndex;
            ClearFeedbackStatus();
        };
        panel.Children.Add(kind);

        panel.Children.Add(Text(I18n.T("feedbackTitle")));
        var title = new TextBox { Text = feedbackTitleDraft, MaxLength = 120 };
        AutomationProperties.SetName(title, I18n.T("feedbackTitle"));
        DeviceId(title, "FeedbackTitle");
        title.TextChanged += (_, _) =>
        {
            if (title.Text == feedbackTitleDraft) return;
            feedbackTitleDraft = title.Text;
            ClearFeedbackStatus();
        };
        panel.Children.Add(title);

        panel.Children.Add(Text(I18n.T("feedbackDetails")));
        var details = new TextBox { Text = feedbackBodyDraft, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 170,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 4000 };
        AutomationProperties.SetName(details, I18n.T("feedbackDetails"));
        DeviceId(details, "FeedbackDetails");
        details.TextChanged += (_, _) =>
        {
            if (details.Text == feedbackBodyDraft) return;
            feedbackBodyDraft = details.Text;
            ClearFeedbackStatus();
        };
        panel.Children.Add(details);

        var message = Text("", 12, true);
        DeviceId(message, "FeedbackStatus");
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        panel.Children.Add(message);
        var send = Action("feedbackSend", async () => await SubmitFeedback(), true);
        send.HorizontalAlignment = HorizontalAlignment.Left;
        send.MinWidth = 135;
        DeviceId(send, "FeedbackSend");
        panel.Children.Add(send);

        refreshFeedback = () =>
        {
            string label = I18n.T(feedbackSending ? "feedbackSending" : "feedbackSend");
            ((TextBlock)send.Content).Text = label;
            AutomationProperties.SetName(send, label);
            kind.IsEnabled = title.IsEnabled = details.IsEnabled = send.IsEnabled = !feedbackSending;
            if (title.Text != feedbackTitleDraft) title.Text = feedbackTitleDraft;
            if (details.Text != feedbackBodyDraft) details.Text = feedbackBodyDraft;
            message.Text = feedbackStatus.Length == 0 ? "" : I18n.T(feedbackStatus);
            message.Visibility = feedbackStatus.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        refreshFeedback();
        body.Children.Add(Card(panel));
        Help(body, "feedbackPrivacy");
    }

    private void ClearFeedbackStatus()
    {
        if (feedbackStatus.Length == 0) return;
        feedbackStatus = "";
        refreshFeedback?.Invoke();
    }

    private async Task SubmitFeedback()
    {
        if (feedbackSending) return;
        string title = feedbackTitleDraft.Trim();
        string details = feedbackBodyDraft.Trim();
        if (title.Length == 0 || details.Length == 0)
        {
            feedbackStatus = "feedbackRequired";
            refreshFeedback?.Invoke();
            return;
        }

        string type = I18n.T(FeedbackKinds[feedbackKindDraft]);
        feedbackSending = true;
        feedbackStatus = "";
        refreshFeedback?.Invoke();
        try
        {
            if (await SendFeedbackAsync(type, title, details))
            {
                feedbackTitleDraft = feedbackBodyDraft = "";
                feedbackStatus = "feedbackSent";
            }
            else feedbackStatus = "feedbackFailed";
        }
        finally
        {
            feedbackSending = false;
            if (!lifetime.IsCancellationRequested) refreshFeedback?.Invoke();
        }
    }

    private async Task<bool> SendFeedbackAsync(string type, string title, string details)
    {
        var client = FeedbackHttpForSmoke ?? FeedbackHttp;
        using var request = new HttpRequestMessage(HttpMethod.Post, WorkerUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { type, title, details }), Encoding.UTF8, "application/json")
        };
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) { return false; }
    }
}
