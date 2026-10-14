using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SnapAsk;

/// Setup window: sign-in status for each AI, Sign in buttons, and how Claude is reached.
/// Shown on first launch and from the tray menu.
public sealed class Accounts : Window
{
    private static Accounts? open;

    public static void ShowWindow()
    {
        if (open == null) { open = new Accounts(); open.Closed += (_, _) => open = null; open.Show(); }
        open.Activate();
    }

    /// Opens the provider's real site (its sign-in page if needed), or `claude auth login` for the CLI route.
    public static void SignIn(Provider p)
    {
        if (p == Provider.Claude && ClaudeCli.InUse) ClaudeCli.OpenLogin();
        else WebBackend.Get(p).ShowFull();
    }

    private readonly Dictionary<Provider, (TextBlock icon, TextBlock status, Button button)> rows = new();
    private readonly TextBlock routeNote = new() { Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };

    private Accounts()
    {
        Title = "SnapAsk setup";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "SnapAsk is running", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = $"Press {HotKeys.Current.Label} anywhere, drag over part of the screen, and ask. SnapAsk lives in the system tray (bottom-right, maybe under ^).",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 16),
        });
        root.Children.Add(new TextBlock { Text = "Accounts", FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Sign in once to each AI you want to use (free accounts work). You stay signed in.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 2, 0, 8),
        });

        foreach (Provider p in Enum.GetValues(typeof(Provider)))
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = "…", VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = p.ToString(), VerticalAlignment = VerticalAlignment.Center };
            var status = new TextBlock { Text = "Checking…", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var btn = new Button { Content = "Sign in…", Padding = new Thickness(10, 3, 10, 3), MinWidth = 80 };
            btn.Click += (_, _) => SignIn(p);
            Grid.SetColumn(name, 1); Grid.SetColumn(status, 2); Grid.SetColumn(btn, 3);
            g.Children.Add(icon); g.Children.Add(name); g.Children.Add(status); g.Children.Add(btn);
            rows[p] = (icon, status, btn);
            root.Children.Add(g);
        }

        var routeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        routeRow.Children.Add(new TextBlock { Text = "Claude via:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var route = new ComboBox { Width = 170, ItemsSource = new[] { "Auto", "claude.ai", "Claude Code CLI" }, SelectedIndex = (int)Settings.Current.ClaudeRoute };
        route.SelectionChanged += (_, _) =>
        {
            Settings.Current.ClaudeRoute = (ClaudeRoute)route.SelectedIndex;
            Settings.Current.Save();
            WebBackend.PrewarmAll();
            _ = Refresh();
        };
        routeRow.Children.Add(route);
        root.Children.Add(routeRow);
        root.Children.Add(routeNote);

        var done = new Button { Content = "Done", IsDefault = true, Padding = new Thickness(18, 4, 18, 4), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        done.Click += (_, _) => Close();
        root.Children.Add(done);
        Content = root;

        timer.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { timer.Start(); await Refresh(); };
        Closed += (_, _) => timer.Stop();
        Settings.Current.Welcomed = true;
        Settings.Current.Save();
    }

    private async System.Threading.Tasks.Task Refresh()
    {
        routeNote.Text = Settings.Current.ClaudeRoute switch
        {
            ClaudeRoute.Auto => ClaudeCli.SignedIn
                ? $"Auto: using your Claude Code CLI ({ClaudeCli.Email ?? "signed in"})."
                : "Auto: no signed-in Claude Code CLI found, so using claude.ai.",
            ClaudeRoute.Cli => "Uses the `claude` command (needs Claude Pro/Max). Slightly faster.",
            _ => "Uses claude.ai: works with any plan, including Free.",
        };
        foreach (var (p, (icon, status, btn)) in rows)
        {
            bool viaCli = p == Provider.Claude && ClaudeCli.InUse;
            bool? state = viaCli ? ClaudeCli.SignedIn : await WebBackend.Get(p).CheckSignedInAsync();
            icon.Text = state == true ? "✔" : state == false ? "!" : "…";
            icon.Foreground = state == true ? Brushes.SeaGreen : state == false ? Brushes.DarkOrange : Brushes.Gray;
            status.Text = viaCli
                ? (state == true ? "via Claude Code CLI" : "CLI not signed in")
                : state == true ? "Signed in" : state == false ? "Not signed in" : "Checking…";
            btn.Content = state == true ? "Open" : "Sign in…";
        }
    }
}
