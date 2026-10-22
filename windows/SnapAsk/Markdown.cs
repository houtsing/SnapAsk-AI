using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapAsk;

/// Lightweight markdown → TextBlock: headings, bullets, **bold**, *italic*, `code`, fenced code blocks.
public static class Markdown
{
    private static readonly Regex Inline = new(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)", RegexOptions.Compiled);
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static void Render(TextBlock tb, string text)
    {
        tb.Inlines.Clear();
        bool inFence = false;
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence)
            {
                tb.Inlines.Add(new Run(line) { FontFamily = Mono, Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });
            }
            else
            {
                var h = Regex.Match(line, @"^#{1,6}\s+(.*)");
                if (h.Success) line = "**" + h.Groups[1].Value + "**";
                var b = Regex.Match(line, @"^(\s*)[-*]\s+(.*)");
                if (b.Success) line = b.Groups[1].Value + "• " + b.Groups[2].Value;
                AddInline(tb, line);
            }
            if (i < lines.Length - 1) tb.Inlines.Add(new LineBreak());
        }
    }

    private static void AddInline(TextBlock tb, string line)
    {
        int pos = 0;
        foreach (Match m in Inline.Matches(line))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(line[pos..m.Index]));
            string t = m.Value;
            if (t.StartsWith("**")) tb.Inlines.Add(new Bold(new Run(t[2..^2])));
            else if (t.StartsWith("`")) tb.Inlines.Add(new Run(t[1..^1]) { FontFamily = Mono });
            else tb.Inlines.Add(new Italic(new Run(t[1..^1])));
            pos = m.Index + m.Length;
        }
        if (pos < line.Length) tb.Inlines.Add(new Run(line[pos..]));
    }
}
