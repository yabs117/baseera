using System.Windows.Automation;
using System.Windows.Threading;
using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private bool sidebarToolsOpen, sidebarCentersOpen;

    private StackPanel SidebarLinks(bool navigationChanged)
    {
        var links = Stack();
        var primary = Stack();
        DeviceId(primary, "SidebarPrimary");
        foreach (string destination in PrimaryPages)
            primary.Children.Add(NavigationButton(destination));
        links.Children.Add(primary);

        var exploration = InvestigationCenters.Except(PrimaryPages).Except(Tools).ToArray();
        if (navigationChanged && Settings.Current.Onboarded)
        {
            if (Tools.Contains(page)) sidebarToolsOpen = true;
            if (exploration.Contains(page)) sidebarCentersOpen = true;
        }
        links.Children.Add(SidebarSection("SidebarTools", "yourTools", "toolbox", Tools, sidebarToolsOpen, open => sidebarToolsOpen = open));
        links.Children.Add(SidebarSection("SidebarCenters", "hubCenters", "centers", exploration, sidebarCentersOpen, open => sidebarCentersOpen = open));
        return links;
    }

    private Expander SidebarSection(string id, string label, string icon, string[] pages, bool isOpen, Action<bool> setOpen)
    {
        var items = Stack();
        DeviceId(items, id);
        foreach (string destination in pages)
        {
            var button = NavigationButton(destination);
            if (expanded) button.Margin = new Thickness(8, 2, 0, 2);
            items.Children.Add(button);
        }

        var section = DisclosureSection(id + "Section", label, icon, items, isOpen, compact: !expanded);
        section.Margin = new Thickness(0, 8, 0, 0);
        bool containsSelection = Settings.Current.Onboarded && pages.Contains(page);
        void UpdateSelection()
        {
            if (!section.IsExpanded && containsSelection) section.SetResourceReference(BackgroundProperty, "Selected");
            else section.Background = Brushes.Transparent;
        }
        UpdateSelection();
        section.Expanded += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, section)) return;
            setOpen(true);
            UpdateSelection();
        };
        section.Collapsed += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, section)) return;
            setOpen(false);
            UpdateSelection();
        };
        return section;
    }

    private Expander DisclosureSection(string id, string labelKey, string icon, FrameworkElement content, bool isOpen, bool compact = false)
    {
        string label = I18n.T(labelKey);
        var header = new DockPanel();
        var glyph = new Border { Width = compact ? 27 : 42, Child = Icons.Create(icon, 18, "Muted") };
        DockPanel.SetDock(glyph, Dock.Left);
        header.Children.Add(glyph);
        if (!compact)
        {
            var title = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = HeadingWeight,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            header.Children.Add(title);
        }

        var section = new Expander
        {
            Style = (Style)FindResource("DisclosureSection"),
            Header = header,
            Content = content,
            IsExpanded = isOpen,
            ToolTip = compact ? label : null
        };
        if (compact) section.Padding = new Thickness(0, 6, 1, 6);
        DeviceId(section, id);
        AutomationProperties.SetName(section, label);
        section.Expanded += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, section)) return;
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (section.IsExpanded && section.IsVisible) section.BringIntoView();
            }, DispatcherPriority.Loaded);
        };
        return section;
    }
}
