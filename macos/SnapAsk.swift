// SnapAsk — press ⌘⇧2, drag over any part of the screen, ask Claude / ChatGPT / Gemini about it.
// Claude goes through the `claude` CLI; ChatGPT and Gemini run their real websites in an embedded web view — all with your own accounts.

import Cocoa
import SwiftUI
import WebKit
import Carbon.HIToolbox
import ServiceManagement
import CoreGraphics
import ApplicationServices

// MARK: - Claude route: claude.ai (any plan, incl. Free) or the Claude Code CLI (Pro/Max, faster)

enum ClaudeRoute: String, CaseIterable, Identifiable {
    case auto, web, cli
    var id: String { rawValue }
    var label: String {
        switch self {
        case .auto: return "Auto"
        case .web: return "claude.ai"
        case .cli: return "Claude Code CLI"
        }
    }

    static var setting: ClaudeRoute {
        get { ClaudeRoute(rawValue: UserDefaults.standard.string(forKey: "claude.backend") ?? "") ?? .auto }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: "claude.backend") }
    }

    /// Result of `claude auth status` at launch: CLI installed and signed in.
    static var cliSignedIn = false
    static var cliEmail: String?

    static var usesCLI: Bool {
        switch setting {
        case .cli: return true
        case .web: return false
        case .auto: return cliSignedIn
        }
    }

    /// Runs `claude auth status` in the background, then calls `done` on the main thread.
    static func detect(_ done: @escaping () -> Void) {
        guard let exe = resolveExecutable("claude") else { dlog("claude CLI not found"); cliSignedIn = false; return done() }
        dlog("checking claude CLI at \(exe)")
        DispatchQueue.global().async {
            let p = Process(), out = Pipe()
            p.executableURL = URL(fileURLWithPath: exe)
            p.arguments = ["auth", "status"]
            p.environment = cliEnvironment
            p.standardOutput = out
            p.standardError = FileHandle.nullDevice
            try? p.run()
            let data = out.fileHandleForReading.readDataToEndOfFile()
            p.waitUntilExit()
            let json = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
            if json == nil { dlog("claude auth status output: \(String(data: data.prefix(300), encoding: .utf8) ?? "?") exit=\(p.terminationStatus)") }
            DispatchQueue.main.async {
                cliSignedIn = json?["loggedIn"] as? Bool == true
                cliEmail = json?["email"] as? String
                dlog("claude CLI signed in=\(cliSignedIn)")
                done()
            }
        }
    }
}

// MARK: - Debug log (~/Library/Logs/SnapAsk.log)

func dlog(_ msg: String) {
    let url = URL(fileURLWithPath: NSHomeDirectory() + "/Library/Logs/SnapAsk.log")
    let line = "\(Date()) \(msg)\n"
    if let h = try? FileHandle(forWritingTo: url) { h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); try? h.close() }
    else { try? line.write(to: url, atomically: true, encoding: .utf8) }
}

// MARK: - Providers & config

enum Provider: String, CaseIterable, Identifiable {
    case claude = "Claude", chatgpt = "ChatGPT", gemini = "Gemini"
    var id: String { rawValue }
    var defaultsKey: String { "model.\(rawValue.lowercased())" }
    /// Optional model override, e.g. `defaults write com.snapask.app model.claude opus`
    var model: String? {
        let m = UserDefaults.standard.string(forKey: defaultsKey)?.trimmingCharacters(in: .whitespaces)
        return (m?.isEmpty ?? true) ? nil : m
    }
    /// Providers driven through their website (signed in with your own account).
    var site: WebSite? {
        switch self {
        case .claude:
            if ClaudeRoute.usesCLI { return nil }   // Claude Code CLI instead of claude.ai
            return WebSite(
                url: "https://claude.ai/new",
                composer: "div.ProseMirror[contenteditable=\"true\"], [data-testid=\"chat-input\"] [contenteditable=\"true\"]",
                send: "button[aria-label=\"Send message\"], button[aria-label=\"Send Message\"]",
                reply: "[data-testid=\"assistant-message\"] .standard-markdown, .font-claude-response .standard-markdown",
                stop: "button[aria-label=\"Stop response\"], [data-is-streaming=\"true\"]")
        case .chatgpt: return WebSite(
            url: "https://chatgpt.com/",
            composer: "#prompt-textarea, .ProseMirror[contenteditable=\"true\"], div[contenteditable=\"true\"][role=\"textbox\"]",
            send: "button[data-testid=\"send-button\"], #composer-submit-button, form button[type=\"submit\"]",
            reply: "[data-markdown-text-style=\"assistant-message\"], [data-message-author-role=\"assistant\"] .markdown",
            stop: "[data-testid=\"stop-button\"], button[aria-label^=\"Stop\"]")
        case .gemini: return WebSite(
            url: "https://gemini.google.com/app",
            composer: "rich-textarea .ql-editor, div.ql-editor[contenteditable=\"true\"]",
            send: "button.send-button, button[aria-label*=\"Send\"]",
            reply: "model-response message-content, model-response .markdown",
            stop: "button[aria-label*=\"Stop\"], button.send-button.stop")
        }
    }
    var loginCommand: String {
        switch self {
        case .claude: return "claude /login"
        case .chatgpt, .gemini: return ""   // signs in on the website inside SnapAsk
        }
    }
}

let systemPrompt = """
The user just captured a region of their screen and is asking you about it. \
Answer the question directly and concisely, based on the screenshot. \
Use light markdown (bold, lists, code) only when it helps. Do not run commands or read files.
"""

/// GUI apps get a minimal PATH, so add the usual CLI install locations.
let cliEnvironment: [String: String] = {
    var env = ProcessInfo.processInfo.environment
    let home = NSHomeDirectory()
    let extra = ["\(home)/.local/bin", "/opt/homebrew/bin", "/usr/local/bin", "\(home)/.npm-global/bin", "\(home)/.bun/bin"]
    env["PATH"] = (extra + [env["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin"]).joined(separator: ":")
    env["NO_COLOR"] = "1"
    return env
}()

func resolveExecutable(_ name: String) -> String? {
    for dir in (cliEnvironment["PATH"] ?? "").split(separator: ":") {
        let p = "\(dir)/\(name)"
        if FileManager.default.isExecutableFile(atPath: p) { return p }
    }
    return nil
}

struct CLIError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

// MARK: - Claude: one long-lived `claude -p` per popup, fed with stream-json (keeps context, streams tokens)

final class ClaudeSession {
    private let proc = Process()
    private let stdin = Pipe(), stdout = Pipe(), stderr = Pipe()
    private var lineBuffer = Data()
    private var errText = ""
    private var sawDelta = false
    private var onDelta: ((String) -> Void)?
    private var onDone: ((String?, String?) -> Void)?
    var onExit: (() -> Void)?

    init(cwd: URL) throws {
        guard let exe = resolveExecutable("claude") else {
            throw CLIError(message: "Claude Code CLI not found. Install it from https://claude.com/claude-code and run `claude` once to sign in.")
        }
        proc.executableURL = URL(fileURLWithPath: exe)
        var args = ["-p", "--input-format", "stream-json", "--output-format", "stream-json",
                    "--include-partial-messages", "--verbose", "--no-session-persistence",
                    "--tools", "", "--setting-sources", "", "--strict-mcp-config",
                    "--system-prompt", systemPrompt]
        if let m = Provider.claude.model { args += ["--model", m] }
        proc.arguments = args
        proc.environment = cliEnvironment
        proc.currentDirectoryURL = cwd
        proc.standardInput = stdin
        proc.standardOutput = stdout
        proc.standardError = stderr

        stdout.fileHandleForReading.readabilityHandler = { [weak self] h in
            let d = h.availableData
            guard !d.isEmpty else { return }
            DispatchQueue.main.async { self?.consume(d) }
        }
        stderr.fileHandleForReading.readabilityHandler = { [weak self] h in
            let d = h.availableData
            guard !d.isEmpty, let s = String(data: d, encoding: .utf8) else { return }
            DispatchQueue.main.async { self?.errText += s }
        }
        proc.terminationHandler = { [weak self] p in
            DispatchQueue.main.async {
                guard let self else { return }
                if let done = self.onDone {
                    self.onDone = nil
                    let tail = self.errText.trimmingCharacters(in: .whitespacesAndNewlines).suffix(600)
                    done(nil, "Claude exited (code \(p.terminationStatus)). \(tail)")
                }
                self.onExit?()
            }
        }
        try proc.run()
    }

    func send(text: String, imageBase64: String?, onDelta: @escaping (String) -> Void,
              onDone: @escaping (String?, String?) -> Void) {
        self.onDelta = onDelta
        self.onDone = onDone
        sawDelta = false
        var content: [[String: Any]] = []
        if let b64 = imageBase64 {
            content.append(["type": "image", "source": ["type": "base64", "media_type": "image/png", "data": b64]])
        }
        content.append(["type": "text", "text": text])
        let msg: [String: Any] = ["type": "user", "message": ["role": "user", "content": content]]
        guard var data = try? JSONSerialization.data(withJSONObject: msg) else { return }
        data.append(0x0A)
        stdin.fileHandleForWriting.write(data)
    }

    private func consume(_ d: Data) {
        lineBuffer.append(d)
        while let nl = lineBuffer.firstIndex(of: 0x0A) {
            let line = lineBuffer[lineBuffer.startIndex..<nl]
            lineBuffer.removeSubrange(lineBuffer.startIndex...nl)
            guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
            handle(obj)
        }
    }

    private func handle(_ obj: [String: Any]) {
        switch obj["type"] as? String {
        case "stream_event":
            if let ev = obj["event"] as? [String: Any], ev["type"] as? String == "content_block_delta",
               let delta = ev["delta"] as? [String: Any], delta["type"] as? String == "text_delta",
               let t = delta["text"] as? String {
                sawDelta = true
                onDelta?(t)
            }
        case "result":
            let done = onDone
            onDone = nil
            if obj["is_error"] as? Bool == true {
                done?(nil, (obj["result"] as? String) ?? "Claude returned an error.")
            } else {
                done?(sawDelta ? nil : obj["result"] as? String, nil)
            }
        default: break
        }
    }

    func stop() {
        stdout.fileHandleForReading.readabilityHandler = nil
        stderr.fileHandleForReading.readabilityHandler = nil
        onDone = nil
        if proc.isRunning { proc.terminate() }
    }
}

// MARK: - ChatGPT & Gemini: the real website in an embedded web view, signed in with your own account

struct WebSite {
    let url: String, composer: String, send: String, reply: String, stop: String
}

final class WebChat: NSObject, WKNavigationDelegate, WKUIDelegate {
    let webView: WKWebView
    let site: WebSite
    // WKWebView's default UA lacks "Safari/…", which makes some sign-in pages refuse it.
    static let userAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Safari/605.1.15"

    init(site: WebSite) {
        self.site = site
        let cfg = WKWebViewConfiguration()
        cfg.websiteDataStore = .default()   // persistent cookies, so you stay signed in
        webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 900, height: 900), configuration: cfg)
        super.init()
        webView.customUserAgent = Self.userAgent
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        webView.load(URLRequest(url: URL(string: site.url)!))
    }

    // MARK: Sign-in popups (Google / Microsoft / Apple / institution SSO) get a real child window,
    // so flows that report back to the opener page keep working.
    private var popups: [NSWindow] = []

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration,
                 for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        let child = WKWebView(frame: NSRect(x: 0, y: 0, width: 520, height: 680), configuration: configuration)
        child.customUserAgent = Self.userAgent
        child.uiDelegate = self
        let w = NSWindow(contentRect: child.frame, styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        w.title = "Sign in"
        w.contentView = child
        w.isReleasedWhenClosed = false
        w.level = .floating
        w.center()
        w.makeKeyAndOrderFront(nil)
        popups.append(w)
        return child
    }

    func webViewDidClose(_ webView: WKWebView) {
        popups.removeAll { w in
            guard w.contentView === webView else { return false }
            w.close(); return true
        }
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        let a = NSAlert(); a.messageText = message; a.runModal(); completionHandler()
    }

    // MARK: Navigation state (so we never script a page that's about to be replaced)

    private var loading = true
    private var afterLoad: [() -> Void] = []

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) { loading = true }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) { loadEnded() }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { loadEnded() }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { loadEnded() }
    private func loadEnded() {
        loading = false
        let q = afterLoad; afterLoad = []
        q.forEach { $0() }
    }
    func whenLoaded(_ f: @escaping () -> Void) { loading ? afterLoad.append(f) : f() }

    /// Navigate and mark as loading immediately, so nothing scripts the outgoing page.
    func navigate(to url: String) {
        loading = true
        webView.load(URLRequest(url: URL(string: url)!))
    }
}

// MARK: - Hidden, pre-loaded ChatGPT / Gemini pages that the popup drives behind the scenes

final class WebBackend {
    static var shared: [Provider: WebBackend] = [:]

    static func get(_ p: Provider) -> WebBackend? {
        guard let site = p.site else { return nil }
        if let b = shared[p] { return b }
        let b = WebBackend(provider: p, site: site)
        shared[p] = b
        return b
    }

    /// Start every web provider loading in the background so the first question is instant.
    static func prewarmAll() { Provider.allCases.forEach { _ = get($0) } }
    static func reloadAll() { shared.values.forEach { $0.reset() } }

    let provider: Provider
    let chat: WebChat
    private let hiddenWindow: NSWindow
    private var fullWindow: NSWindow?
    private(set) var owner: UUID?        // the popup whose conversation is currently on the page
    private var fresh = false            // page is an untouched new chat
    private var generation = 0           // bumps on reset; stale polls stop

    init(provider: Provider, site: WebSite) {
        self.provider = provider
        chat = WebChat(site: site)
        // An off-screen window keeps the page fully alive (laid out, timers running) without being visible.
        hiddenWindow = NSWindow(contentRect: NSRect(x: -10000, y: -10000, width: 900, height: 900),
                                styleMask: [.borderless], backing: .buffered, defer: false)
        hiddenWindow.isReleasedWhenClosed = false
        hiddenWindow.ignoresMouseEvents = true
        hiddenWindow.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        hiddenWindow.contentView = chat.webView
        // Pages that think they're hidden stop rendering streamed replies; tell WebKit to ignore occlusion.
        let occl = NSSelectorFromString("_setWindowOcclusionDetectionEnabled:")
        if chat.webView.responds(to: occl) {
            typealias SetBool = @convention(c) (AnyObject, Selector, Bool) -> Void
            unsafeBitCast(chat.webView.method(for: occl), to: SetBool.self)(chat.webView, occl, false)
        }
        hiddenWindow.orderBack(nil)
        hiddenWindow.setFrameOrigin(NSPoint(x: -10000, y: -10000))
        fresh = true
    }

    /// true = signed in, false = not signed in, nil = page still loading / unknown.
    func checkSignedIn(_ done: @escaping (Bool?) -> Void) {
        chat.webView.callAsyncJavaScript(Self.signedInJS, arguments: ["composer": chat.site.composer], in: nil, in: .page) { r in
            done((try? r.get()) as? Bool)
        }
    }

    private static let signedInJS = """
    const vis = e => e && e.getClientRects().length > 0;
    if (/\\/(login|logout|auth|signin|sign-in)/i.test(location.pathname) || /^(accounts|auth|login)\\./.test(location.hostname)) return false;
    const loginButton = [...document.querySelectorAll('a, button')].some(e => vis(e) &&
      /^(log ?in|sign ?in|sign up|get started)$/i.test((e.innerText || e.getAttribute('aria-label') || '').trim()));
    if (loginButton) return false;
    return document.querySelector(composer) ? true : null;
    """

    /// Back to a blank new chat, ready for the next snapshot.
    func reset() {
        generation += 1
        owner = nil
        fresh = true
        chat.navigate(to: chat.site.url)
    }

    func release(_ id: UUID) { if owner == id { reset() } }

    /// Shows the real page (full conversation, images, model picker…) in a normal window.
    func showFull() {
        if fullWindow == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 760),
                             styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
            w.title = provider.rawValue
            w.isReleasedWhenClosed = false
            w.center()
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { [weak self] _ in
                guard let self else { return }
                self.hiddenWindow.contentView = self.chat.webView
            }
            fullWindow = w
        }
        fullWindow?.contentView = chat.webView
        NSApp.activate(ignoringOtherApps: true)
        fullWindow?.makeKeyAndOrderFront(nil)
    }

    /// Sends one question. `image` is attached when non-nil. Streams the reply text via `onUpdate`.
    func ask(owner id: UUID, text: String, image: Data?, onUpdate: @escaping (String) -> Void,
             onDone: @escaping (String?) -> Void) {
        dlog("\(provider.rawValue) ask owner=\(owner?.uuidString.prefix(4) ?? "nil") me=\(id.uuidString.prefix(4)) fresh=\(fresh)")
        let go = { [weak self] in
            guard let self else { return }
            dlog("\(self.provider.rawValue) page ready, sending")
            self.owner = id
            self.fresh = false
            let gen = self.generation
            self.chat.webView.callAsyncJavaScript(Self.askJS, arguments: [
                "composer": self.chat.site.composer, "send": self.chat.site.send, "reply": self.chat.site.reply,
                "b64": image?.base64EncodedString() ?? "", "text": text,
            ], in: nil, in: .page) { r in
                dlog("\(self.provider.rawValue) askJS => \(r) gen=\(gen)/\(self.generation)")
                guard gen == self.generation else { return }
                guard case .success(let v) = r, let d = v as? [String: Any], let before = d["before"] as? Int else {
                    let code: String
                    switch r {
                    case .success(let v): code = (v as? [String: Any])?["error"] as? String ?? "unknown"
                    case .failure(let e): code = e.localizedDescription
                    }
                    dlog("\(self.provider.rawValue) ask failed: \(code)")
                    return onDone(code == "not-signed-in"
                        ? "\(self.provider.rawValue) isn't signed in yet. Sign in once, then ask again."
                        : "Couldn't send to \(self.provider.rawValue) (\(code)). Try “Open full chat”.")
                }
                self.poll(gen: gen, before: before, last: "", stable: 0, started: Date(), onUpdate: onUpdate, onDone: onDone)
            }
        }
        if owner == id || fresh {
            chatWhenLoaded(go)
        } else {
            // Another popup's conversation is on the page; start a new chat first.
            reset()
            chatWhenLoaded(go)
        }
    }

    private func chatWhenLoaded(_ f: @escaping () -> Void) { chat.whenLoaded(f) }

    private func poll(gen: Int, before: Int, last: String, stable: Int, started: Date,
                      onUpdate: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
            guard let self, gen == self.generation else { return }
            self.chat.webView.callAsyncJavaScript(Self.readJS, arguments: [
                "reply": self.chat.site.reply, "stop": self.chat.site.stop, "before": before,
            ], in: nil, in: .page) { r in
                guard gen == self.generation else { return }
                let d = (try? r.get()) as? [String: Any]
                let text = d?["text"] as? String ?? ""
                let streaming = d?["streaming"] as? Bool ?? true
                if text != last { onUpdate(text) }
                let stableNow = (text == last && !text.isEmpty) ? stable + 1 : 0
                if !text.isEmpty && !streaming && stableNow >= 6 { return onDone(nil) }        // ~1.5 s with no change
                if Date().timeIntervalSince(started) > 300 { return onDone(text.isEmpty ? "Timed out waiting for a reply." : nil) }
                self.poll(gen: gen, before: before, last: text, stable: stableNow, started: started, onUpdate: onUpdate, onDone: onDone)
            }
        }
    }

    /// Wait for the composer, attach the image (synthetic paste → file input → drop), type, press send.
    private static let askJS = """
    const sleep = ms => new Promise(r => setTimeout(r, ms));
    const $ = s => document.querySelector(s);
    let ed = null;
    for (let i = 0; i < 80 && !(ed = $(composer)); i++) await sleep(250);
    if (!ed) return {error: 'not-signed-in'};
    const before = document.querySelectorAll(reply).length;
    // Clear any saved draft left in the box.
    ed.focus();
    { const s0 = window.getSelection(); s0.selectAllChildren(ed); document.execCommand('delete'); }
    if (b64) {
      const bin = atob(b64), u8 = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
      const file = new File([u8], 'snapshot.png', {type: 'image/png', lastModified: Date.now()});
      const mk = () => { const dt = new DataTransfer(); dt.items.add(file); return dt; };
      const previews = () => document.querySelectorAll('img[src^="blob:"], img[src^="data:image"], form img').length;
      const base = previews();
      const appeared = async () => { for (let i = 0; i < 12; i++) { if (previews() > base) return true; await sleep(250); } return false; };
      ed.focus();
      let ok = false;
      try { ed.dispatchEvent(new ClipboardEvent('paste', {clipboardData: mk(), bubbles: true, cancelable: true})); } catch (e) {}
      ok = await appeared();
      if (!ok) for (const inp of document.querySelectorAll('input[type=file]')) {
        if (inp.accept && !(inp.accept.includes('image') || inp.accept.includes('*'))) continue;
        try { inp.files = mk().files; inp.dispatchEvent(new Event('change', {bubbles: true})); } catch (e) {}
        if (ok = await appeared()) break;
      }
      if (!ok) {
        try { const dt = mk(); for (const t of ['dragenter', 'dragover', 'drop'])
          ed.dispatchEvent(new DragEvent(t, {dataTransfer: dt, bubbles: true, cancelable: true})); } catch (e) {}
        ok = await appeared();
      }
      if (!ok) return {error: 'attach-failed'};
    }
    ed.focus();
    const s = window.getSelection(); s.selectAllChildren(ed); s.collapseToEnd();
    document.execCommand('insertText', false, text);
    await sleep(150);
    let tried = false;
    for (let i = 0; i < 240; i++) {      // waits for the image upload to finish
      if (tried && (!ed.isConnected || !ed.innerText.trim())) return {before};   // sent (by click or Enter)
      const b = [...document.querySelectorAll(send)].find(b =>
        !b.disabled && b.getAttribute('aria-disabled') !== 'true' && b.getClientRects().length > 0);
      if (b) {
        tried = true;
        b.click();
        // Confirm it actually went: the box empties once the message is sent. Otherwise retry (or press Enter).
        for (let k = 0; k < 8; k++) {
          await sleep(150);
          if (!ed.isConnected || !ed.innerText.trim()) return {before};
        }
        if (i % 4 === 3) ed.dispatchEvent(new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true}));
        continue;
      }
      await sleep(250);
    }
    return {error: 'send-timeout'};
    """

    /// Latest reply as markdown-ish text (keeps bullets, bold, code, LaTeX) + whether it's still streaming.
    private static let readJS = """
    const msgs = document.querySelectorAll(reply);
    if (msgs.length <= before) return {text: '', streaming: true};
    const root = msgs[msgs.length - 1];
    const out = [];
    const block = new Set(['P','DIV','UL','OL','TABLE','TR','BLOCKQUOTE','H1','H2','H3','H4','H5','H6','SECTION']);
    const depth = n => { let d = -1; for (let p = n; p && p !== root; p = p.parentElement) if (p.tagName === 'UL' || p.tagName === 'OL') d++; return Math.max(d, 0); };
    const walk = n => {
      if (n.nodeType === 3) { out.push(n.nodeValue); return; }
      if (n.nodeType !== 1) return;
      const t = n.tagName;
      if (['BUTTON', 'svg', 'STYLE', 'SCRIPT', 'NOSCRIPT'].includes(t)) return;
      if (n.classList.contains('katex')) {
        const a = n.querySelector('annotation');
        out.push(a ? (n.closest('.katex-display') ? '\\n$$' + a.textContent + '$$\\n' : '$' + a.textContent + '$') : n.innerText);
        return;
      }
      if (t === 'PRE') { out.push('\\n```\\n' + (n.querySelector('code') || n).innerText.trim() + '\\n```\\n'); return; }
      if (t === 'CODE') { out.push('`' + n.innerText + '`'); return; }
      if (t === 'BR') { out.push('\\n'); return; }
      if (/^H[1-6]$/.test(t)) out.push('\\n## ');
      if (t === 'LI') {
        const p = n.parentElement;
        out.push('\\n' + '  '.repeat(depth(n)) + (p && p.tagName === 'OL' ? ([...p.children].indexOf(n) + 1) + '. ' : '• '));
      }
      const wrap = {STRONG: '**', B: '**', EM: '*'}[t];
      if (wrap) out.push(wrap);
      for (const c of n.childNodes) walk(c);
      if (wrap) out.push(wrap);
      if (t === 'TD' || t === 'TH') out.push(' | ');
      if (block.has(t)) out.push('\\n');
    };
    walk(root);
    const text = out.join('').replace(/[ \\t]+\\n/g, '\\n').replace(/\\n(• |\\d+\\. )\\n+/g, '\\n$1').replace(/\\n{2,}(?=\\s*(• |\\d+\\. ))/g, '\\n').replace(/\\n{3,}/g, '\\n\\n').trim();
    return {text, streaming: !!document.querySelector(stop)};
    """
}

/// Minimal browser chrome for the sign-in window: back / forward / reload / start over + editable address bar.
struct BrowserView: View {
    let web: WebChat
    let home: String
    @State private var address = ""
    let tick = Timer.publish(every: 0.5, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 6) {
                Button { web.webView.goBack() } label: { Image(systemName: "chevron.left") }
                Button { web.webView.goForward() } label: { Image(systemName: "chevron.right") }
                Button { web.webView.reload() } label: { Image(systemName: "arrow.clockwise") }
                TextField("Address", text: $address)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit {
                        var a = address.trimmingCharacters(in: .whitespaces)
                        if !a.contains("://") { a = "https://" + a }
                        if let u = URL(string: a) { web.webView.load(URLRequest(url: u)) }
                    }
                Button("Start over") {
                    WKWebsiteDataStore.default().fetchDataRecords(ofTypes: [WKWebsiteDataTypeCookies]) { _ in
                        web.webView.load(URLRequest(url: URL(string: home)!))
                    }
                }
            }
            .padding(8)
            Divider()
            WebViewHost(webView: web.webView)
        }
        .frame(minWidth: 700, minHeight: 500)
        .onReceive(tick) { _ in
            if let u = web.webView.url?.absoluteString, u != address, !isEditing { address = u }
        }
    }
    private var isEditing: Bool { NSApp.keyWindow?.firstResponder is NSTextView }
}

struct WebViewHost: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ nsView: WKWebView, context: Context) {}
}

// MARK: - Conversation model

struct Msg: Identifiable {
    let id = UUID()
    var role: String          // "You" or provider name
    var text: String
    var isError = false
    var signIn: Provider?      // show a "Sign in to …" button under this (error) message
    var isUser: Bool { role == "You" }
}

final class ChatModel: ObservableObject {
    @Published var messages: [Msg] = []
    @Published var input = ""
    @Published var busy = false
    @Published var provider: Provider {
        didSet { UserDefaults.standard.set(provider.rawValue, forKey: "provider") }
    }
    let id = UUID()
    private var webSynced: [Provider: Int] = [:]   // messages[0..<n] already on that site's page

    let dir: URL
    let imageURL: URL
    let image: NSImage
    private lazy var imageBase64: String = (try? Data(contentsOf: imageURL))?.base64EncodedString() ?? ""
    private var claude: ClaudeSession?
    private var claudeSynced = 0            // messages[0..<claudeSynced] already known to the Claude session

    init(dir: URL, imageURL: URL) {
        self.dir = dir
        self.imageURL = imageURL
        self.image = NSImage(contentsOf: imageURL) ?? NSImage()
        self.provider = Provider(rawValue: UserDefaults.standard.string(forKey: "provider") ?? "") ?? .claude
    }

    private func transcript(_ history: [Msg], then question: String) -> String {
        let turns = history.filter { !$0.isError && !$0.text.isEmpty }
        guard !turns.isEmpty else { return question }
        let past = turns.map { "\($0.isUser ? "User" : "Assistant"): \($0.text)" }.joined(separator: "\n\n")
        return "Earlier in this conversation about the screenshot:\n\n\(past)\n\nNew question: \(question)"
    }

    func send() {
        let q = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty, !busy else { return }
        input = ""
        let prior = messages
        messages.append(Msg(role: "You", text: q))
        messages.append(Msg(role: provider.rawValue, text: ""))
        let i = messages.count - 1
        busy = true

        let onDelta: (String) -> Void = { [weak self] d in self?.messages[i].text += d }
        let onReplace: (String) -> Void = { [weak self] t in self?.messages[i].text = t }
        let onDone: (String?, String?) -> Void = { [weak self] full, err in
            guard let self else { return }
            if let full, self.messages[i].text.isEmpty { self.messages[i].text = full }
            if let err {
                let e = err.trimmingCharacters(in: .whitespacesAndNewlines)
                self.messages[i].text += (self.messages[i].text.isEmpty ? "" : "\n\n") + "⚠️ " + e
                self.messages[i].isError = true
                if e.contains("isn't signed in"), let p = Provider(rawValue: self.messages[i].role) { self.messages[i].signIn = p }
            }
            self.messages[i].text = self.messages[i].text.trimmingCharacters(in: .whitespacesAndNewlines)
            if self.messages[i].text.isEmpty { self.messages[i].text = "(empty response)" }
            self.busy = false
        }

        do {
            switch provider {
            case .claude where provider.site == nil: try askClaude(q, prior: prior, onDelta: onDelta, onDone: onDone)
            case .claude
            , .chatgpt, .gemini: askWeb(provider, q, prior: prior, onReplace: onReplace, onDone: onDone)
            }
        } catch {
            onDone(nil, error.localizedDescription)
        }
    }

    private func askClaude(_ q: String, prior: [Msg], onDelta: @escaping (String) -> Void,
                           onDone: @escaping (String?, String?) -> Void) throws {
        let isNew = claude == nil
        if isNew {
            let s = try ClaudeSession(cwd: dir)
            s.onExit = { [weak self, weak s] in
                if let self, self.claude === s { self.claude = nil; self.claudeSynced = 0 }
            }
            claude = s
            claudeSynced = 0
        }
        // Replay only turns that happened with other providers since Claude last saw the chat.
        let missed = Array(prior[min(claudeSynced, prior.count)...])
        claude?.send(text: transcript(missed, then: q), imageBase64: isNew ? imageBase64 : nil,
                     onDelta: onDelta, onDone: onDone)
        claudeSynced = messages.count
    }

    private func askWeb(_ p: Provider, _ q: String, prior: [Msg], onReplace: @escaping (String) -> Void,
                        onDone: @escaping (String?, String?) -> Void) {
        guard let backend = WebBackend.get(p) else { return }
        // New conversation on the page if this popup doesn't own it (or it was reset): send image + context.
        let isFirst = backend.owner != id
        if isFirst { webSynced[p] = 0 }
        let missed = Array(prior[min(webSynced[p] ?? 0, prior.count)...])
        var text = transcript(missed, then: q)
        if isFirst { text = systemPrompt + "\n\n" + text }
        backend.ask(owner: id, text: text, image: isFirst ? (try? Data(contentsOf: imageURL)) : nil,
                    onUpdate: onReplace) { [weak self] err in
            if err == nil, let self { self.webSynced[p] = self.messages.count }
            onDone(nil, err)
        }
    }

    func shutdown() {
        claude?.stop()
        Provider.allCases.forEach { WebBackend.shared[$0]?.release(id) }
        try? FileManager.default.removeItem(at: dir)
    }
}

// MARK: - UI

/// Inline markdown via AttributedString; headings become bold lines.
func renderMarkdown(_ s: String) -> AttributedString {
    let lines = s.components(separatedBy: "\n").map { line -> String in
        if let r = line.range(of: #"^#{1,6}\s+"#, options: .regularExpression) {
            return "**\(line[r.upperBound...])**"
        }
        if let r = line.range(of: #"^(\s*)[-*]\s+"#, options: .regularExpression) {
            let indent = line[r].prefix { $0 == " " }
            return "\(indent)• \(line[r.upperBound...])"
        }
        return line
    }
    let src = lines.joined(separator: "\n")
    return (try? AttributedString(markdown: src, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace)))
        ?? AttributedString(s)
}

struct MessageRow: View {
    let msg: Msg
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(msg.role).font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            if msg.text.isEmpty {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.mini)
                    Text("Thinking…").foregroundStyle(.secondary)
                }
            } else {
                Text(renderMarkdown(msg.text))
                    .foregroundStyle(msg.isError ? Color.red : Color.primary)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                if let p = msg.signIn {
                    Button("Sign in to \(p.rawValue)…") { AppDelegate.shared.signIn(to: p) }
                        .padding(.top, 4)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(msg.isUser ? 0 : 10)
        .background(msg.isUser ? Color.clear : Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 8))
    }
}

struct ChatView: View {
    @ObservedObject var model: ChatModel
    var onClose: () -> Void
    @FocusState private var focused: Bool
    @State private var contentHeight: CGFloat = 0

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: 10) {
                Image(nsImage: model.image)
                    .resizable().aspectRatio(contentMode: .fit)
                    .frame(maxWidth: 150, maxHeight: 80, alignment: .leading)
                    .clipShape(RoundedRectangle(cornerRadius: 6))
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.primary.opacity(0.15)))
                    .onTapGesture { NSWorkspace.shared.open(model.imageURL) }
                    .help("Open snapshot in Preview")
                Spacer(minLength: 0)
                VStack(alignment: .trailing, spacing: 8) {
                    HStack(spacing: 6) {
                        Picker("", selection: $model.provider) {
                            ForEach(Provider.allCases) { Text($0.rawValue).tag($0) }
                        }
                        .pickerStyle(.segmented).labelsHidden().frame(width: 240)
                        Button(action: onClose) { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                            .buttonStyle(.plain).help("Close (Esc)")
                    }
                    HStack(spacing: 10) {
                        if model.provider.site != nil {
                            Button("Open full chat ↗") { WebBackend.get(model.provider)?.showFull() }
                                .buttonStyle(.link).font(.caption2)
                        }
                        Text("⌘1 / ⌘2 / ⌘3 to switch").font(.caption2).foregroundStyle(.tertiary)
                    }
                }
            }
            .padding(12)

            if !model.messages.isEmpty {
                Divider()
                ScrollViewReader { proxy in
                    ScrollView {
                        VStack(alignment: .leading, spacing: 12) {
                            ForEach(model.messages) { MessageRow(msg: $0) }
                            Color.clear.frame(height: 1).id("bottom")
                        }
                        .padding(12)
                        .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { contentHeight = $0 }
                    }
                    .frame(height: min(max(contentHeight, 60), 460))
                    .onChange(of: model.messages.last?.text) { proxy.scrollTo("bottom", anchor: .bottom) }
                }
            }

            Divider()
            HStack(spacing: 8) {
                TextField(model.messages.isEmpty ? "Ask about this snapshot…" : "Follow-up…", text: $model.input, axis: .vertical)
                    .textFieldStyle(.plain).font(.system(size: 14))
                    .lineLimit(1...6)
                    .focused($focused)
                    .onSubmit { model.send() }
                if model.busy {
                    ProgressView().controlSize(.small)
                } else {
                    Button { model.send() } label: {
                        Image(systemName: "arrow.up.circle.fill").font(.system(size: 20))
                    }
                    .buttonStyle(.plain)
                    .disabled(model.input.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            .padding(12)
        }
        .frame(width: 520)
        .background(.regularMaterial)
        .background(
            // Hidden buttons for ⌘1/⌘2/⌘3 provider shortcuts.
            HStack {
                ForEach(Array(Provider.allCases.enumerated()), id: \.offset) { idx, p in
                    Button("") { model.provider = p }
                        .keyboardShortcut(KeyEquivalent(Character("\(idx + 1)")), modifiers: .command)
                }
            }.opacity(0)
        )
        .onAppear { DispatchQueue.main.async { focused = true } }
        .onChange(of: model.busy) { if !model.busy { focused = true } }
    }
}

// MARK: - Popup panel

final class AskPanel: NSPanel, NSWindowDelegate {
    let model: ChatModel
    private var topLeft: NSPoint = .zero

    init(model: ChatModel, near point: NSPoint) {
        self.model = model
        super.init(contentRect: NSRect(x: 0, y: 0, width: 520, height: 140),
                   styleMask: [.titled, .closable, .resizable, .fullSizeContentView, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
        standardWindowButton(.closeButton)?.isHidden = true
        standardWindowButton(.miniaturizeButton)?.isHidden = true
        standardWindowButton(.zoomButton)?.isHidden = true
        isMovableByWindowBackground = true
        level = .floating
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        delegate = self

        let host = NSHostingController(rootView: ChatView(model: model, onClose: { [weak self] in self?.close() }))
        host.sizingOptions = [.preferredContentSize]
        contentViewController = host

        // Place just below-right of where the selection ended, kept on screen.
        let screen = NSScreen.screens.first { $0.frame.contains(point) } ?? NSScreen.main!
        let vf = screen.visibleFrame
        var tl = NSPoint(x: point.x + 12, y: point.y - 12)
        tl.x = min(max(tl.x, vf.minX + 8), vf.maxX - 528)
        tl.y = min(max(tl.y, vf.minY + 320), vf.maxY - 8)
        topLeft = tl
        setFrameTopLeftPoint(tl)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    override func cancelOperation(_ sender: Any?) { close() }

    // Grow downward as the conversation gets longer; flip upward if we'd run off the screen.
    // Moving the window while AppKit is mid-layout (this callback can fire inside a layout pass) makes it
    // re-run layout recursively until it aborts the app, so reposition on the next run-loop turn instead.
    private var repositionPending = false

    func windowDidResize(_ notification: Notification) {
        guard !repositionPending else { return }
        repositionPending = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.repositionPending = false
            guard let vf = self.screen?.visibleFrame ?? NSScreen.main?.visibleFrame else { return }
            var tl = self.topLeft
            if tl.y - self.frame.height < vf.minY { tl.y = vf.minY + self.frame.height }
            tl.y = min(tl.y, vf.maxY)
            let current = NSPoint(x: self.frame.minX, y: self.frame.maxY)
            if abs(current.x - tl.x) > 0.5 || abs(current.y - tl.y) > 0.5 { self.setFrameTopLeftPoint(tl) }
        }
    }
    func windowDidMove(_ notification: Notification) {
        topLeft = NSPoint(x: frame.minX, y: frame.maxY)
    }
    func windowWillClose(_ notification: Notification) {
        model.shutdown()
        AppDelegate.shared.panels.removeAll { $0 === self }
    }
}

// MARK: - Hotkey choices

struct HotKeyChoice {
    let label: String, keyCode: Int, mods: Int
    var isFn = false   // fn can't be a Carbon hotkey modifier; handled by FnHotKey's event tap instead
    static let all: [HotKeyChoice] = [
        .init(label: "fn⌃S", keyCode: kVK_ANSI_S, mods: 0, isFn: true),
        .init(label: "⌘⇧2", keyCode: kVK_ANSI_2, mods: cmdKey | shiftKey),
        .init(label: "⌃⇧Space", keyCode: kVK_Space, mods: controlKey | shiftKey),
        .init(label: "⌥⇧A", keyCode: kVK_ANSI_A, mods: optionKey | shiftKey),
        .init(label: "⌘⌥A", keyCode: kVK_ANSI_A, mods: cmdKey | optionKey),
        .init(label: "⌃⌥⌘S", keyCode: kVK_ANSI_S, mods: controlKey | optionKey | cmdKey),
    ]
    static var current: HotKeyChoice {
        all.first { $0.label == UserDefaults.standard.string(forKey: "hotkey") } ?? all[0]
    }
}

/// fn+⌃S via a CGEvent tap (needs Accessibility permission). The keystroke is swallowed so ⌃S doesn't reach the app.
enum FnHotKey {
    static var tap: CFMachPort?
    static var source: CFRunLoopSource?
    static var retry: Timer?

    static func start() {
        stop()
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(opts)
        install()
    }

    private static func install() {
        let mask = CGEventMask(1 << CGEventType.keyDown.rawValue)
        guard let t = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                        eventsOfInterest: mask, callback: { _, type, event, _ in
            if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                if let t = FnHotKey.tap { CGEvent.tapEnable(tap: t, enable: true) }
                return Unmanaged.passUnretained(event)
            }
            let f = event.flags
            if event.getIntegerValueField(.keyboardEventKeycode) == Int64(kVK_ANSI_S), !f.intersection([.maskControl, .maskSecondaryFn, .maskCommand, .maskAlternate]).isEmpty {
                dlog("S keyDown flags=0x\(String(f.rawValue, radix: 16))")
            }
            if event.getIntegerValueField(.keyboardEventKeycode) == Int64(kVK_ANSI_S),
               f.contains(.maskSecondaryFn), f.contains(.maskControl),
               !f.contains(.maskCommand), !f.contains(.maskAlternate), !f.contains(.maskShift) {
                if event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                    dlog("fn⌃S pressed")
                    DispatchQueue.main.async { AppDelegate.shared.snap() }
                }
                return nil
            }
            return Unmanaged.passUnretained(event)
        }, userInfo: nil) else {
            // No Accessibility permission yet — keep retrying until it's granted.
            retry = Timer.scheduledTimer(withTimeInterval: 2, repeats: false) { _ in install() }
            return
        }
        tap = t
        source = CFMachPortCreateRunLoopSource(nil, t, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: t, enable: true)
        dlog("fn⌃S event tap installed")
    }

    static func stop() {
        retry?.invalidate(); retry = nil
        if let s = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), s, .commonModes) }
        if let t = tap { CGEvent.tapEnable(tap: t, enable: false) }
        tap = nil; source = nil
    }
}

// MARK: - Welcome / status window (shown when you open the app)

struct WelcomeView: View {
    @State private var hotkey = HotKeyChoice.current.label
    @State private var screenOK = CGPreflightScreenCaptureAccess()
    @State private var axOK = AXIsProcessTrusted()
    @State private var claudeRoute = ClaudeRoute.setting
    @State private var signedIn: [Provider: Bool?] = [:]
    let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    private func refreshAccounts() {
        for p in Provider.allCases {
            if p == .claude && ClaudeRoute.usesCLI { signedIn[p] = ClaudeRoute.cliSignedIn; continue }
            WebBackend.get(p)?.checkSignedIn { signedIn[p] = $0 }
        }
    }

    @ViewBuilder private func accountRow(_ p: Provider) -> some View {
        let state = signedIn[p] ?? nil
        let viaCLI = p == .claude && ClaudeRoute.usesCLI
        HStack {
            Image(systemName: state == true ? "checkmark.circle.fill" : state == false ? "person.crop.circle.badge.exclamationmark" : "circle.dotted")
                .foregroundStyle(state == true ? .green : state == false ? .orange : .secondary)
            Text(p.rawValue).frame(width: 64, alignment: .leading)
            Text(viaCLI ? (state == true ? "via Claude Code CLI" : "CLI not signed in — run `claude` in Terminal")
                        : state == true ? "Signed in" : state == false ? "Not signed in" : "Checking…")
                .font(.callout).foregroundStyle(.secondary)
            Spacer()
            if !viaCLI {
                Button(state == true ? "Open" : "Sign in…") { AppDelegate.shared.signIn(to: p) }
            }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                Image(systemName: "text.viewfinder").font(.system(size: 28))
                VStack(alignment: .leading) {
                    Text("SnapAsk setup").font(.headline)
                    Text("It lives in your menu bar (viewfinder icon).").font(.caption).foregroundStyle(.secondary)
                }
            }
            HStack {
                Text("Shortcut:")
                Picker("", selection: $hotkey) {
                    ForEach(HotKeyChoice.all, id: \.label) { Text($0.label).tag($0.label) }
                }
                .labelsHidden().frame(width: 130)
                .onChange(of: hotkey) {
                    UserDefaults.standard.set(hotkey, forKey: "hotkey")
                    AppDelegate.shared.registerHotKey()
                }
            }
            HStack {
                Image(systemName: screenOK ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .foregroundStyle(screenOK ? .green : .orange)
                Text(screenOK ? "Screen Recording permission granted" : "Screen Recording permission needed")
                Spacer()
                if !screenOK {
                    Button("Grant…") { AppDelegate.shared.requestScreenAccess() }
                }
            }
            if HotKeyChoice.current.isFn || hotkey.hasPrefix("fn") {
                HStack {
                    Image(systemName: axOK ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .foregroundStyle(axOK ? .green : .orange)
                    Text(axOK ? "Accessibility permission granted" : "Accessibility permission needed for fn shortcut")
                    Spacer()
                    if !axOK {
                        Button("Grant…") {
                            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
                        }
                    }
                }
            }
            if !screenOK {
                Text("After enabling SnapAsk in System Settings → Privacy & Security → Screen & System Audio Recording, quit and reopen SnapAsk.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Divider()
            Text("Accounts").font(.headline)
            Text("Sign in once to each AI you want to use (free accounts work). You stay signed in.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            ForEach(Provider.allCases) { accountRow($0) }
            HStack {
                Text("Claude via:")
                Picker("", selection: $claudeRoute) {
                    ForEach(ClaudeRoute.allCases) { Text($0.label).tag($0) }
                }
                .labelsHidden().frame(width: 160)
                .onChange(of: claudeRoute) {
                    ClaudeRoute.setting = claudeRoute
                    WebBackend.prewarmAll()
                    refreshAccounts()
                }
            }
            Text(claudeRoute == .auto
                 ? (ClaudeRoute.cliSignedIn ? "Auto: using your Claude Code CLI (\(ClaudeRoute.cliEmail ?? "signed in"))." : "Auto: no signed-in Claude Code CLI found, so using claude.ai.")
                 : claudeRoute == .cli ? "Uses the `claude` command (needs Claude Pro/Max). Slightly faster." : "Uses claude.ai — works with any plan, including Free.")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("Snap now") { AppDelegate.shared.closeWelcome(); AppDelegate.shared.snap() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(18)
        .frame(width: 420)
        .onAppear(perform: refreshAccounts)
        .onReceive(timer) { t in
            screenOK = CGPreflightScreenCaptureAccess(); axOK = AXIsProcessTrusted()
            if Int(t.timeIntervalSince1970) % 3 == 0 { refreshAccounts() }
        }
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate {
    static let shared = AppDelegate()
    var statusItem: NSStatusItem!
    var panels: [AskPanel] = []
    private var hotKeyRef: EventHotKeyRef?
    private var capturing = false
    let cacheRoot = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("SnapAsk", isDirectory: true)

    func applicationDidFinishLaunching(_ n: Notification) {
        try? FileManager.default.removeItem(at: cacheRoot)   // clear leftovers from a previous run
        dlog("launched")
        setupStatusItem()
        registerHotKey()
        ClaudeRoute.detect { WebBackend.prewarmAll() }
        // Test hook: `osascript -e 'tell application "SnapAsk" to activate'` won't work for accessory apps, so listen here.
        DistributedNotificationCenter.default().addObserver(forName: .init("com.snapask.snap"), object: nil, queue: .main) { _ in self.snap() }
        installDebugHooks()
        dlog("screen access=\(CGPreflightScreenCaptureAccess())")
        if !CGPreflightScreenCaptureAccess() { requestScreenAccess() }
        if !UserDefaults.standard.bool(forKey: "welcomed") || !CGPreflightScreenCaptureAccess() {
            UserDefaults.standard.set(true, forKey: "welcomed")
            showWelcome()
        }
    }

    // Opening the app again (Finder / Spotlight / Dock) shows the status window.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWelcome()
        return true
    }

    var welcome: NSWindow?

    func showWelcome() {
        if welcome == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: WelcomeView()))
            w.title = "SnapAsk"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.center()
            welcome = w
        }
        NSApp.activate(ignoringOtherApps: true)
        welcome?.makeKeyAndOrderFront(nil)
    }

    func closeWelcome() { welcome?.close() }

    func requestScreenAccess() {
        if !CGRequestScreenCaptureAccess() {
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!)
        }
    }

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "text.viewfinder", accessibilityDescription: "SnapAsk")
        rebuildMenu()
    }

    func rebuildMenu() {
        let menu = NSMenu()
        let snap = NSMenuItem(title: "Snap & Ask   \(HotKeyChoice.current.label)", action: #selector(snapFromMenu), keyEquivalent: "")
        snap.target = self
        menu.addItem(snap)
        let settings = NSMenuItem(title: "Setup & Accounts…", action: #selector(showWelcomeFromMenu), keyEquivalent: "")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(.separator())

        let current = Provider(rawValue: UserDefaults.standard.string(forKey: "provider") ?? "") ?? .claude
        let defItem = NSMenuItem(title: "Default model", action: nil, keyEquivalent: "")
        let defMenu = NSMenu()
        for p in Provider.allCases {
            let it = NSMenuItem(title: p.rawValue, action: #selector(setDefault(_:)), keyEquivalent: "")
            it.target = self; it.representedObject = p.rawValue
            it.state = p == current ? .on : .off
            defMenu.addItem(it)
        }
        defItem.submenu = defMenu
        menu.addItem(defItem)

        let signIn = NSMenuItem(title: "Sign in", action: nil, keyEquivalent: "")
        let signMenu = NSMenu()
        for p in Provider.allCases {
            let title = p.site.map { "\(p.rawValue)  (\(URL(string: $0.url)!.host!))" } ?? "\(p.rawValue)  (\(p.loginCommand))"
            let it = NSMenuItem(title: title, action: #selector(signIn(_:)), keyEquivalent: "")
            it.target = self; it.representedObject = p.rawValue
            signMenu.addItem(it)
        }
        signIn.submenu = signMenu
        menu.addItem(signIn)

        let login = NSMenuItem(title: "Launch at Login", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
        login.target = self
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit SnapAsk", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        statusItem.menu = menu
    }

    @objc private func setDefault(_ sender: NSMenuItem) {
        UserDefaults.standard.set(sender.representedObject as? String, forKey: "provider")
        rebuildMenu()
    }

    @objc private func signIn(_ sender: NSMenuItem) {
        guard let p = Provider(rawValue: sender.representedObject as? String ?? "") else { return }
        if p.site != nil { return showWebSignIn(p) }
        let path = cliEnvironment["PATH"] ?? ""
        let script = """
        tell application "Terminal"
            activate
            do script "export PATH='\(path)'; \(p.loginCommand)"
        end tell
        """
        var err: NSDictionary?
        NSAppleScript(source: script)?.executeAndReturnError(&err)
    }

    private var webSignIns: [Provider: (window: NSWindow, web: WebChat)] = [:]

    func signIn(to p: Provider) {
        if p.site != nil { showWebSignIn(p) }
        else { // CLI route
            let script = "tell application \"Terminal\" to activate\ntell application \"Terminal\" to do script \"export PATH='\(cliEnvironment["PATH"] ?? "")'; claude auth login\""
            var err: NSDictionary?
            NSAppleScript(source: script)?.executeAndReturnError(&err)
        }
    }

    private func showWebSignIn(_ p: Provider) {
        guard let site = p.site else { return }
        if webSignIns[p] == nil {
            let web = WebChat(site: site)
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 960, height: 760),
                             styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
            w.title = "Sign in to \(p.rawValue) — close this window when you're done"
            w.contentViewController = NSHostingController(rootView: BrowserView(web: web, home: site.url))
            w.setContentSize(NSSize(width: 960, height: 760))
            w.isReleasedWhenClosed = false
            w.center()
            webSignIns[p] = (w, web)
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { [weak self] _ in
                self?.webSignIns[p] = nil
                WebBackend.reloadAll()   // pick up the new sign-in
            }
        }
        NSApp.activate(ignoringOtherApps: true)
        webSignIns[p]?.window.makeKeyAndOrderFront(nil)
    }

    @objc private func toggleLaunchAtLogin() {
        do {
            if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
            else { try SMAppService.mainApp.register() }
        } catch {
            let a = NSAlert(); a.messageText = "Couldn't change Launch at Login"; a.informativeText = error.localizedDescription; a.runModal()
        }
        rebuildMenu()
    }

    @objc private func snapFromMenu() { snap() }
    @objc private func showWelcomeFromMenu() { showWelcome() }

    private var handlerInstalled = false

    func registerHotKey() {
        if !handlerInstalled {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetEventDispatcherTarget(), { _, _, _ in
                dlog("hotkey pressed")
                DispatchQueue.main.async { AppDelegate.shared.snap() }
                return noErr
            }, 1, &spec, nil, nil)
            handlerInstalled = true
        }
        if let ref = hotKeyRef { UnregisterEventHotKey(ref); hotKeyRef = nil }
        let hk = HotKeyChoice.current
        if hk.isFn {
            FnHotKey.start()
            if statusItem != nil { rebuildMenu() }
            return
        }
        FnHotKey.stop()
        let id = EventHotKeyID(signature: OSType(0x534E_4150), id: 1)   // 'SNAP'
        let status = RegisterEventHotKey(UInt32(hk.keyCode), UInt32(hk.mods), id,
                                         GetEventDispatcherTarget(), 0, &hotKeyRef)
        dlog("RegisterEventHotKey \(hk.label) status=\(status)")
        if statusItem != nil { rebuildMenu() }
    }

    func snap() {
        dlog("snap() capturing=\(capturing)")
        guard !capturing else { return }
        capturing = true
        let dir = cacheRoot.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let shot = dir.appendingPathComponent("snapshot.png")

        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        p.arguments = ["-i", "-x", "-o", shot.path]   // interactive region (Space = window), no sound, no shadow
        p.terminationHandler = { proc in
            dlog("screencapture exited status=\(proc.terminationStatus)")
            DispatchQueue.main.async {
                self.capturing = false
                let size = (try? FileManager.default.attributesOfItem(atPath: shot.path)[.size] as? Int) ?? 0
                guard size > 0 else { try? FileManager.default.removeItem(at: dir); return }   // cancelled
                self.showPanel(dir: dir, shot: shot)
            }
        }
        do { try p.run() } catch { dlog("screencapture failed: \(error)"); capturing = false }
    }

    // Developer hooks (distributed notifications) for testing without a human at the keyboard.
    private func installDebugHooks() {
        let dnc = DistributedNotificationCenter.default()
        dnc.addObserver(forName: .init("com.snapask.debug.open"), object: nil, queue: .main) { n in
            guard let arg = n.object as? String else { return }
            // "path" or "path|x,y" (anchor point in screen coordinates)
            let parts = arg.split(separator: "|").map(String.init)
            let src = parts[0]
            var anchor: NSPoint?
            if parts.count > 1 {
                let xy = parts[1].split(separator: ",").compactMap { Double($0) }
                if xy.count == 2 { anchor = NSPoint(x: xy[0], y: xy[1]) }
            }
            let dir = self.cacheRoot.appendingPathComponent(UUID().uuidString, isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let shot = dir.appendingPathComponent("snapshot.png")
            try? FileManager.default.copyItem(at: URL(fileURLWithPath: src), to: shot)
            self.showPanel(dir: dir, shot: shot, at: anchor)
        }
        dnc.addObserver(forName: .init("com.snapask.debug.eval"), object: nil, queue: .main) { n in
            let p = Provider(rawValue: UserDefaults.standard.string(forKey: "provider") ?? "") ?? .chatgpt
            guard let js = n.object as? String, let web = WebBackend.get(p)?.chat else { return dlog("eval: no web backend") }
            web.webView.callAsyncJavaScript(js, arguments: [:], in: nil, in: .page) { r in dlog("eval => \(r)") }
        }
        dnc.addObserver(forName: .init("com.snapask.debug.shot"), object: nil, queue: .main) { n in
            guard let out = n.object as? String, let win = self.panels.last ?? self.welcome else { return }
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
            p.arguments = ["-x", "-o", "-l", "\(win.windowNumber)", out]
            try? p.run(); p.waitUntilExit()
            dlog("shot => \(out)")
        }
        dnc.addObserver(forName: .init("com.snapask.debug.ask"), object: nil, queue: .main) { n in
            guard let q = n.object as? String, let panel = self.panels.last else { return }
            let parts = q.split(separator: "|", maxSplits: 1).map(String.init)
            if parts.count == 2, let p = Provider(rawValue: parts[0]) { panel.model.provider = p }
            panel.model.input = parts.last ?? q
            panel.model.send()
        }
        dnc.addObserver(forName: .init("com.snapask.debug.dump"), object: nil, queue: .main) { _ in
            guard let panel = self.panels.last else { return }
            for m in panel.model.messages { dlog("[\(m.role)] \(m.text)") }
        }
        dnc.addObserver(forName: .init("com.snapask.debug.welcome"), object: nil, queue: .main) { _ in self.showWelcome() }
        dnc.addObserver(forName: .init("com.snapask.debug.closeall"), object: nil, queue: .main) { _ in
            self.panels.forEach { $0.close() }
            self.welcome?.close()
        }
    }

    private func showPanel(dir: URL, shot: URL, at anchor: NSPoint? = nil) {
        let panel = AskPanel(model: ChatModel(dir: dir, imageURL: shot), near: anchor ?? NSEvent.mouseLocation)
        panels.append(panel)
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
app.delegate = AppDelegate.shared
app.run()
