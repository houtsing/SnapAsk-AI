using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace SnapAsk;

/// The "ask about this snapshot" window. An ordinary window: others can cover it, and the
/// bring-back shortcut (or the taskbar / Alt+Tab) returns to it.
public sealed class Popup : Window
{
    private sealed class Msg
    {
        public string Role = "";   // "You", provider name, or "" for a note
        public string Text = "";
        public bool IsError;
        public List<byte[]> Images = new();
        public TextBlock? View;
        public StackPanel? Box;
        public bool IsUser => Role == "You";
        public bool IsNote => Role.Length == 0;
    }

    /// Open popups, most recently used last (the one the bring-back shortcut returns to).
    public static readonly List<Popup> Open = new();

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
    private readonly Dictionary<Provider, string> chatUrl = new();   // this popup's conversation on each site
    private readonly HashSet<Provider> snapshotSent = new();     // the snapshot is already in that conversation
    private readonly Dictionary<Provider, List<ChatRef>> chats = new();
    private bool claudeWeb;                                     // Claude via claude.ai here (a claude.ai chat was resumed)
    private readonly List<byte[]> pending = new();               // images for the next message
    private readonly WrapPanel pendingRow = new() { Margin = new Thickness(12, 10, 12, 0), Visibility = Visibility.Collapsed };
    private readonly Dictionary<Provider, Button> tabs = new();
    private Provider provider;
    private bool busy;
    private ClaudeCli.Session? claudeCli;
    private int claudeCliSynced;

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
        ShowInTaskbar = true;
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
        full.Click += (_, _) => Accounts.SignIn(provider);
        var chatsButton = new Button { Content = "Chats ▾", Foreground = Fg, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 11, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, Template = FlatButton(), ToolTip = "Start a new chat or continue a saved one" };
        chatsButton.Click += (_, _) => ShowChatsMenu(chatsButton);
        var hint = new TextBlock { Text = "Ctrl+1/2/3", Foreground = Dim, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ctrl+1 / 2 / 3 switch between Claude, ChatGPT and Gemini" };

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { tabBox, close } });
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { chatsButton, full, hint } });

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
        // Ctrl+V with an image (or image files) on the clipboard attaches it to the next message.
        CommandManager.AddPreviewExecutedHandler(input, (_, e) =>
        {
            if (e.Command == ApplicationCommands.Paste && PasteImages()) e.Handled = true;
        });
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
        var snapButton = new Button { Content = "📷", Width = 28, Height = 28, Foreground = Dim, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = FlatButton(), ToolTip = "Snap another area of the screen and attach it" };
        snapButton.Click += (_, _) => SnapAttachment();
        var inputGrid = new Grid { Margin = new Thickness(12) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.Children.Add(input);
        inputGrid.Children.Add(placeholder);
        Grid.SetColumn(snapButton, 1);
        snapButton.Margin = new Thickness(8, 0, 0, 0);
        inputGrid.Children.Add(snapButton);
        Grid.SetColumn(sendButton, 2);
        sendButton.Margin = new Thickness(8, 0, 0, 0);
        inputGrid.Children.Add(sendButton);
        var inputArea = new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = new StackPanel { Children = { pendingRow, inputGrid } } };
        AllowDrop = true;
        Drop += (_, e) => { foreach (var img in ImagesFrom(e.Data)) AddPending(img); };

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
        Loaded += (_, _) => { Activate(); input.Focus(); _ = LoadChatsAsync(); };
        Activated += (_, _) => { Open.Remove(this); Open.Add(this); };
        Closed += (_, _) =>
        {
            Open.Remove(this);
            claudeCli?.Dispose();
            WebBackend.ReleaseAll(id);
        };
        Open.Add(this);
        SetProvider(provider);
    }

    private void SetProvider(Provider p)
    {
        provider = p;
        Settings.Current.Provider = p;
        Settings.Current.Save();
        foreach (var (tp, b) in tabs) b.Background = tp == p ? Accent : Brushes.Transparent;
        if (IsLoaded) _ = LoadChatsAsync();
    }

    private bool UsesWeb(Provider p) => p != Provider.Claude || !ClaudeCli.InUse || claudeWeb;

    /// Bring-back shortcut: in front and ready to type; if it already is, back behind your work.
    public void BringBack(bool toggle)
    {
        if (toggle && IsActive && WindowState != WindowState.Minimized) { WindowState = WindowState.Minimized; return; }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        input.Focus();
    }

    // ---- Resuming a saved conversation ----

    private async Task LoadChatsAsync()
    {
        var p = provider;
        var list = await WebBackend.Get(p).ListChatsAsync();
        if (list.Count > 0 || !chats.ContainsKey(p)) chats[p] = list;
    }

    private void ShowChatsMenu(Button anchorButton)
    {
        var menu = new ContextMenu { PlacementTarget = anchorButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var fresh = new MenuItem { Header = "New chat", IsEnabled = !busy };
        fresh.Click += (_, _) => NewChat();
        menu.Items.Add(fresh);
        menu.Items.Add(new Separator());
        var list = chats.GetValueOrDefault(provider) ?? new List<ChatRef>();
        if (list.Count == 0) menu.Items.Add(new MenuItem { Header = chats.ContainsKey(provider) ? "No saved chats found" : "Loading…", IsEnabled = false });
        else
        {
            menu.Items.Add(new MenuItem { Header = $"Continue a {provider} chat:", IsEnabled = false });
            foreach (var c in list)
            {
                var it = new MenuItem { Header = c.Title.Replace("_", "__"), IsEnabled = !busy };
                it.Click += (_, _) => Resume(c);
                menu.Items.Add(it);
            }
        }
        menu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "Refresh list" };
        refresh.Click += async (_, _) => { await LoadChatsAsync(); ShowChatsMenu(anchorButton); };
        menu.Items.Add(refresh);
        menu.IsOpen = true;
    }

    /// Continue `c` instead of starting a new chat: the next question (with the snapshot) goes there.
    private void Resume(ChatRef c)
    {
        var p = provider;
        if (busy) return;
        if (p == Provider.Claude && ClaudeCli.InUse) claudeWeb = true;
        chatUrl[p] = c.Url;
        synced[p] = 0;              // anything already asked here gets passed along as context
        snapshotSent.Remove(p);
        _ = WebBackend.Get(p).OpenAsync(id, c.Url);
        AddMessage(new Msg { Text = $"↪ Continuing “{c.Title}” in {p}" });
        input.Focus();
    }

    private void NewChat()
    {
        var p = provider;
        if (busy) return;
        if (p == Provider.Claude) { claudeWeb = false; claudeCli?.Dispose(); claudeCli = null; claudeCliSynced = messages.Count; }
        chatUrl.Remove(p);
        synced[p] = messages.Count;   // a clean slate: don't replay this popup's earlier turns
        snapshotSent.Remove(p);
        if (WebBackend.Existing(p) is { } b && b.Owner == id) _ = b.ResetAsync();
        AddMessage(new Msg { Text = $"↪ New {p} chat" });
        input.Focus();
    }

    // ---- Extra images (paste, drop, camera button) ----

    private bool PasteImages()
    {
        var found = ImagesFrom(Clipboard.GetDataObject());
        foreach (var img in found) AddPending(img);
        return found.Count > 0;
    }

    /// Images in a clipboard / drag payload: image files, or bitmap data (unless it's text with a picture attached).
    private static List<byte[]> ImagesFrom(IDataObject? data)
    {
        var result = new List<byte[]>();
        if (data == null) return result;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files)
            {
                try
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
                        result.Add(ToPng(new BitmapImage(new Uri(f))));
                }
                catch (Exception ex) { Log.Write($"couldn't read {f}: {ex.Message}"); }
            }
            return result;
        }
        if (data.GetDataPresent(DataFormats.UnicodeText)) return result;
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
            result.Add(ToPng(bmp));
        return result;
    }

    private static byte[] ToPng(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private void SnapAttachment()
    {
        if (busy) return;
        Hide();
        try
        {
            var picked = RegionSelector.Pick();
            if (picked is { } p)
            {
                using var ms = new MemoryStream();
                p.image.Save(ms, ImageFormat.Png);
                AddPending(ms.ToArray());
            }
        }
        catch (Exception ex) { Log.Write($"capture failed: {ex}"); }
        finally { BringBack(false); }
    }

    private void AddPending(byte[] img)
    {
        pending.Add(img);
        RenderPending();
        input.Focus();
    }

    private void RenderPending()
    {
        pendingRow.Children.Clear();
        foreach (var img in pending.ToList())
        {
            var remove = new Button { Content = "✕", FontSize = 9, Width = 16, Height = 16, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = RoundButton(), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, -4, 0), ToolTip = "Remove" };
            remove.Click += (_, _) => { pending.Remove(img); RenderPending(); };
            pendingRow.Children.Add(new Grid { Margin = new Thickness(0, 0, 8, 0), Children = { Thumb(img), remove } });
        }
        pendingRow.Visibility = pending.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Image Thumb(byte[] png) =>
        new() { Source = ToBitmapSource(png), MaxHeight = 44, MaxWidth = 100, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };

    /// The snapshot (first time in a conversation), images from turns the AI hasn't seen, and new ones.
    private List<byte[]> ImagesToSend(bool snapshot, List<Msg> missed, List<byte[]> extra)
    {
        var list = new List<byte[]>();
        if (snapshot) list.Add(png);
        list.AddRange(missed.SelectMany(m => m.Images));
        list.AddRange(extra);
        return list;
    }

    // ---- Conversation ----

    private void Send()
    {
        string q = input.Text.Trim();
        if (q.Length == 0 || busy) return;
        input.Text = "";
        var extra = pending.ToList();
        pending.Clear();
        RenderPending();
        var prior = messages.ToList();
        AddMessage(new Msg { Role = "You", Text = q, Images = extra });
        var reply = new Msg { Role = provider.ToString() };
        AddMessage(reply);
        SetBusy(true);

        var p = provider;
        if (!UsesWeb(p)) { AskClaudeCli(q, prior, extra, reply); return; }
        var backend = WebBackend.Get(p);
        // No conversation of ours on this site yet (or it was reset): start one, with the snapshot and context.
        bool isNew = !chatUrl.ContainsKey(p) && backend.Owner != id;
        if (isNew) { synced[p] = 0; snapshotSent.Remove(p); }
        var missed = prior.Skip(Math.Min(synced.GetValueOrDefault(p), prior.Count)).ToList();
        string text = Transcript(missed, q);
        if (isNew) text = Sites.SystemPrompt + "\n\n" + text;
        var images = ImagesToSend(!snapshotSent.Contains(p), missed, extra);

        _ = backend.AskAsync(id, chatUrl.GetValueOrDefault(p), text, images, t => Dispatcher.Invoke(() => Update(reply, t)))
            .ContinueWith(task => Dispatcher.Invoke(() =>
            {
                string? err = task.IsFaulted ? task.Exception?.GetBaseException().Message : task.Result;
                if (err == null)
                {
                    synced[p] = messages.Count;
                    snapshotSent.Add(p);
                    if (backend.CurrentChatUrl is { } u) chatUrl[p] = u;
                    _ = LoadChatsAsync();
                }
                Finish(reply, err, p);
            }));
    }

    private void Finish(Msg reply, string? err, Provider p)
    {
        if (err != null)
        {
            reply.IsError = true;
            Update(reply, (reply.Text.Length > 0 ? reply.Text + "\n\n" : "") + "⚠ " + err);
            if (err.Contains("isn't signed in"))
            {
                var b = new Button { Content = $"Sign in to {p}…", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
                b.Click += (_, _) => Accounts.SignIn(p);
                reply.Box?.Children.Add(b);
            }
        }
        else if (reply.Text.Length == 0) Update(reply, "(empty response)");
        SetBusy(false);
    }

    private void AskClaudeCli(string q, List<Msg> prior, List<byte[]> extra, Msg reply)
    {
        try
        {
            bool isNew = claudeCli == null || !claudeCli.Alive;
            if (isNew) { claudeCli?.Dispose(); claudeCli = new ClaudeCli.Session(); claudeCliSynced = 0; }
            var missed = prior.Skip(Math.Min(claudeCliSynced, prior.Count)).ToList();
            claudeCliSynced = messages.Count;
            _ = claudeCli!.AskAsync(Transcript(missed, q), ImagesToSend(isNew, missed, extra),
                    d => Dispatcher.Invoke(() => Update(reply, reply.Text + d)))
                .ContinueWith(t => Dispatcher.Invoke(() =>
                    Finish(reply, t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result, Provider.Claude)));
        }
        catch (Exception ex) { Finish(reply, ex.Message, Provider.Claude); }
    }

    private static string Transcript(List<Msg> history, string question)
    {
        var turns = history.Where(m => !m.IsError && !m.IsNote && m.Text.Length > 0).ToList();
        if (turns.Count == 0) return question;
        string past = string.Join("\n\n", turns.Select(m => $"{(m.IsUser ? "User" : "Assistant")}: {m.Text}"));
        return $"Earlier in this conversation about the screenshot:\n\n{past}\n\nNew question: {question}";
    }

    private void AddMessage(Msg m)
    {
        messages.Add(m);
        listArea.Visibility = Visibility.Visible;
        placeholder.Text = "Follow-up…  (Ctrl+V pastes an image)";
        if (m.IsNote)
        {
            list.Children.Add(new TextBlock { Text = m.Text, Foreground = Dim, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
            scroller.ScrollToEnd();
            return;
        }
        var role = new TextBlock { Text = m.Role, Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        m.View = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Fg };
        var stack = new StackPanel { Children = { role } };
        if (m.Images.Count > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var img in m.Images) { var t = Thumb(img); t.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(t); }
            stack.Children.Add(row);
        }
        stack.Children.Add(m.View);
        m.Box = stack;
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
