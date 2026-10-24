using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed class RepairApprovalWindow : Window
{
    internal RepairApproval? Approval { get; private set; }
    private readonly CheckBox acknowledge = new();
    internal RepairApprovalWindow(Window owner, string actionId)
    {
        var action = RepairCatalog.Get(actionId); var start = RepairRunner.StartInfo(action);
        Owner = owner; Title = I18n.T("smartApproveTitle"); Width = 650; Height = 530;
        MinWidth = 540; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon;
        FlowDirection = FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        var root = new DockPanel { Margin = new Thickness(24), FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight }; Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var confirm = new Button { Content = I18n.T("smartExecute"), IsEnabled = false };
        var cancel = new Button { Content = I18n.T("cancel"), IsCancel = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(confirm, "SmartConfirmRepair");
        confirm.Click += (_, _) =>
        {
            if (acknowledge.IsChecked != true) return;
            Approval = RepairApproval.Confirm(action.Id, acknowledge.IsChecked == true); DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Add(string text, int size = 13) => body.Children.Add(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        Add(I18n.T("smartApproveTitle"), 22); Add(I18n.T("repair-" + action.Id), 16); Add(I18n.T("repairEffect-" + action.Id));
        body.Children.Add(new TextBox { Text = start.FileName + " " + string.Join(" ", start.ArgumentList), IsReadOnly = true,
            Height = double.NaN, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.LeftToRight });
        if (action.Administrator) Add(I18n.T("smartAdmin"));
        if (action.Network) Add(I18n.T("smartNetwork"));
        if (action.LongRunning) Add(I18n.T("smartLongRunning"));
        Add(I18n.T("smartNoGuarantee"));
        acknowledge.Content = new TextBlock { Text = I18n.T("smartAcknowledge"), TextWrapping = TextWrapping.Wrap }; acknowledge.IsChecked = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(acknowledge, "SmartAcknowledgeRepair");
        acknowledge.Checked += (_, _) => confirm.IsEnabled = true; acknowledge.Unchecked += (_, _) => confirm.IsEnabled = false;
        body.Children.Add(acknowledge); Loaded += (_, _) => cancel.Focus();
    }
}
