using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace SnapAsk;

/// The floating "ask about this snapshot" window.
public sealed class Popup : Window
{
    private sealed class Msg
    {
        public string Role = "";   // "You" or provider name
        public string Text = "";
        public bool IsError;
        public TextBlock? View;
        public bool IsUser => Role == "You";
    }

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x2B, 0x2A, 0x28));
    private static readonly Brush Card = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
    private static readonly Brush Fg = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9A, 0x99, 0x96));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush Divider = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    private readonly Guid id = Guid.NewGuid();
    private readonly byte[] png;
    private readonly System.Drawing.Point anchor;
    private readonly List<Msg> messages = new();
    private readonly Dictionary<Provider, int> synced = new();   // messages already on each site's page
    private readonly Dictionary<Provider, Button> tabs = new();
    private Provider provider;
    private bool busy;

    private readonly StackPanel list = new() { Margin = new Thickness(12) };
    private readonly ScrollViewer scroller;
    private readonly Border listArea;
    private readonly TextBox input;
    private readonly TextBlock placeholder;
    private readonly Button sendButton;

    public Popup(System.Drawing.Bitmap shot, System.Drawing.Point anchorPx)
    {
        using (var ms = new MemoryStream()) { shot.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
        anchor = anchorPx;
        provider = Settings.Current.Provider;

        Title = "SnapAsk";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Foreground = Fg;

        // ---- Header: thumbnail + model tabs ----
        var thumb = new Image
        {
            Source = ToBitmapSource(png), MaxWidth = 150, MaxHeight = 80, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, ToolTip = "Open snapshot",
        };
        thumb.MouseLeftButtonUp += (_, _) => OpenSnapshot();

        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var tabBox = new Border { CornerRadius = new CornerRadius(8), Background = Card, Padding = new Thickness(2), Child = tabRow };
        foreach (Provider p in Enum.GetValues(typeof(Provider)))
        {
            var b = new Button
            {
                Content = p.ToString(), Padding = new Thickness(12, 4, 12, 4), BorderThickness = new Thickness(0),
                Foreground = Fg, Cursor = Cursors.Hand, Template = FlatButton(),
            };
            b.Click += (_, _) => SetProvider(p);
            tabs[p] = b;
            tabRow.Children.Add(b);
        }
        var close = new Button { Content = "✕", Foreground = Dim, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Template = FlatButton(), ToolTip = "Close (Esc)" };
        close.Click += (_, _) => Close();
        var full = new Button { Content = "Open full chat ↗", Foreground = Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Template = FlatButton() };
        full.Click += (_, _) => WebBackend.Get(provider).ShowFull();
        var hint = new TextBlock { Text = "Ctrl+1 / 2 / 3 to switch", Foreground = Dim, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { tabBox, close } });
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { full, hint } });

        var header = new Grid { Margin = new Thickness(12), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(thumb);
        Grid.SetColumn(right, 1);
        header.Children.Add(right);
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // ---- Conversation ----
        scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 };
        listArea = new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = scroller, Visibility = Visibility.Collapsed };

        // ---- Input ----
        input = new TextBox
        {
            Background = Brushes.Transparent, Foreground = Fg, CaretBrush = Fg, BorderThickness = new Thickness(0),
            AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MaxHeight = 120, VerticalAlignment = VerticalAlignment.Center,
        };
        placeholder = new TextBlock { Text = "Ask about this snapshot…", Foreground = Dim, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                int c = input.CaretIndex; input.Text = input.Text.Insert(c, "\n"); input.CaretIndex = c + 1; e.Handled = true;
            }
            else if (e.Key == Key.Enter) { e.Handled = true; Send(); }
        };
        sendButton = new Button { Content = "↑", Width = 28, Height = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Background = Accent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = RoundButton() };
        sendButton.Click += (_, _) => Send();
        var inputGrid = new Grid { Margin = new Thickness(12) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.Children.Add(input);
        inputGrid.Children.Add(placeholder);
        Grid.SetColumn(sendButton, 1);
        sendButton.Margin = new Thickness(8, 0, 0, 0);
        inputGrid.Children.Add(sendButton);
        var inputArea = new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = inputGrid };

        var root = new StackPanel();
        root.Children.Add(header);
        root.Children.Add(listArea);
        root.Children.Add(inputArea);
        Content = new Border
        {
            Background = Bg, CornerRadius = new CornerRadius(14), BorderBrush = Divider, BorderThickness = new Thickness(1),
            Child = root, Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 },
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.D1 or Key.D2 or Key.D3)
            {
                SetProvider((Provider)(e.Key - Key.D1)); e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => PlaceNearAnchor();
        Loaded += (_, _) => { Activate(); input.Focus(); };
        Closed += (_, _) => { foreach (Provider p in Enum.GetValues(typeof(Provider))) WebBackend.Get(p).Release(id); };
        SetProvider(provider);
    }

    private void SetProvider(Provider p)
    {
        provider = p;
        Settings.Current.Provider = p;
        Settings.Current.Save();
        foreach (var (tp, b) in tabs) b.Background = tp == p ? Accent : Brushes.Transparent;
    }

    // ---- Conversation ----

    private void Send()
    {
        string q = input.Text.Trim();
        if (q.Length == 0 || busy) return;
        input.Text = "";
        var prior = messages.ToList();
        AddMessage(new Msg { Role = "You", Text = q });
        var reply = new Msg { Role = provider.ToString() };
        AddMessage(reply);
        SetBusy(true);

        var p = provider;
        var backend = WebBackend.Get(p);
        bool isFirst = backend.Owner != id;   // new chat on that site: send the image + earlier context
        if (isFirst) synced[p] = 0;
        var missed = prior.Skip(Math.Min(synced.GetValueOrDefault(p), prior.Count)).ToList();
        string text = Transcript(missed, q);
        if (isFirst) text = Sites.SystemPrompt + "\n\n" + text;

        _ = backend.AskAsync(id, text, isFirst ? png : null, t => Dispatcher.Invoke(() => Update(reply, t)))
            .ContinueWith(task => Dispatcher.Invoke(() =>
            {
                string? err = task.IsFaulted ? task.Exception?.GetBaseException().Message : task.Result;
                if (err != null)
                {
                    reply.IsError = true;
                    Update(reply, (reply.Text.Length > 0 ? reply.Text + "\n\n" : "") + "⚠ " + err);
                }
                else
                {
                    synced[p] = messages.Count;
                    if (reply.Text.Length == 0) Update(reply, "(empty response)");
                }
                SetBusy(false);
            }));
    }

    private static string Transcript(List<Msg> history, string question)
    {
        var turns = history.Where(m => !m.IsError && m.Text.Length > 0).ToList();
        if (turns.Count == 0) return question;
        string past = string.Join("\n\n", turns.Select(m => $"{(m.IsUser ? "User" : "Assistant")}: {m.Text}"));
        return $"Earlier in this conversation about the screenshot:\n\n{past}\n\nNew question: {question}";
    }

    private void AddMessage(Msg m)
    {
        messages.Add(m);
        listArea.Visibility = Visibility.Visible;
        placeholder.Text = "Follow-up…";
        var role = new TextBlock { Text = m.Role, Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        m.View = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Fg };
        var stack = new StackPanel { Children = { role, m.View } };
        list.Children.Add(m.IsUser
            ? new Border { Child = stack, Margin = new Thickness(0, 0, 0, 12) }
            : new Border { Child = stack, Background = Card, CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) });
        Update(m, m.Text);
    }

    private void Update(Msg m, string text)
    {
        m.Text = text;
        if (m.View == null) return;
        if (text.Length == 0 && !m.IsUser) { m.View.Text = "Thinking…"; m.View.Foreground = Dim; }
        else
        {
            m.View.Foreground = m.IsError ? Brushes.IndianRed : Fg;
            Markdown.Render(m.View, text);
        }
        scroller.ScrollToEnd();
    }

    private void SetBusy(bool b)
    {
        busy = b;
        sendButton.Content = b ? "…" : "↑";
        sendButton.IsEnabled = !b;
        if (!b) input.Focus();
    }

    // ---- Helpers ----

    private void OpenSnapshot()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SnapAsk-{id:N}.png");
        File.WriteAllBytes(path, png);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static BitmapSource ToBitmapSource(byte[] bytes)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(bytes);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static ControlTemplate FlatButton()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        return t;
    }

    private static ControlTemplate RoundButton()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        return t;
    }

    // Place just below-right of where the selection ended, kept inside that monitor's work area (in physical pixels).
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void PlaceNearAnchor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = VisualTreeHelper.GetDpi(this);
        var wa = WinForms.Screen.FromPoint(anchor).WorkingArea;
        int w = (int)(Width * dpi.DpiScaleX), h = (int)(380 * dpi.DpiScaleY);
        int x = Math.Clamp(anchor.X + 12, wa.Left + 8, Math.Max(wa.Left + 8, wa.Right - w - 8));
        int y = Math.Clamp(anchor.Y + 12, wa.Top + 8, Math.Max(wa.Top + 8, wa.Bottom - h - 8));
        const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
