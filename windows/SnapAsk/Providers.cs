using System.IO;
using System.Reflection;

namespace SnapAsk;

public enum Provider { Claude, ChatGPT, Gemini }

/// CSS selectors for driving each site's own chat page. These change when the sites are redesigned.
public sealed record Site(string Url, string Composer, string Send, string Reply, string Stop);

public static class Sites
{
    public static Site For(Provider p) => p switch
    {
        Provider.Claude => new Site(
            "https://claude.ai/new",
            "div.ProseMirror[contenteditable=\"true\"], [data-testid=\"chat-input\"] [contenteditable=\"true\"]",
            "button[aria-label=\"Send message\"], button[aria-label=\"Send Message\"]",
            ".font-claude-response, [data-testid=\"assistant-message\"]",
            "button[aria-label=\"Stop response\"], [data-is-streaming=\"true\"]"),
        Provider.ChatGPT => new Site(
            "https://chatgpt.com/",
            "#prompt-textarea, .ProseMirror[contenteditable=\"true\"], div[contenteditable=\"true\"][role=\"textbox\"]",
            "button[data-testid=\"send-button\"], #composer-submit-button, form button[type=\"submit\"]",
            "[data-markdown-text-style=\"assistant-message\"], [data-message-author-role=\"assistant\"] .markdown",
            "[data-testid=\"stop-button\"], button[aria-label^=\"Stop\"]"),
        _ => new Site(
            "https://gemini.google.com/app",
            "rich-textarea .ql-editor, div.ql-editor[contenteditable=\"true\"]",
            "button.send-button, button[aria-label*=\"Send\"]",
            "model-response message-content, model-response .markdown",
            "button[aria-label*=\"Stop\"], button.send-button.stop"),
    };

    public const string SystemPrompt =
        "The user just captured a region of their screen and is asking you about it. " +
        "Answer the question directly and concisely, based on the screenshot. " +
        "Use light markdown (bold, lists, code) only when it helps.";

    public static readonly string AskJs = Load("ask.js");
    public static readonly string ReadJs = Load("read.js");

    private static string Load(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
