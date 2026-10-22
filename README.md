# SnapAsk AI

**Snapshot any part of your screen and ask Claude, ChatGPT or Gemini about it in one step, from any app, on macOS and Windows.**

Press a shortcut, drag over what you want to ask about (a lecture slide, a chart, an error message, a paragraph in a PDF), and a small popup appears right there, a bit like macOS's *Look Up / Define*. Type your question and the answer appears in the popup. Ask follow-ups, or switch to a different AI mid-conversation.

![SnapAsk popup](docs/screenshot.png)

---

## Why

Asking an AI about something on screen normally takes several steps: take a screenshot, open the AI's website, paste it in, type the question, then switch back. Chrome's built-in "Ask Gemini" helps, but it only works in Chrome and only with Gemini.

SnapAsk turns that into **one shortcut**. It works in any app (Preview, PowerPoint, Zoom, a terminal, anything), with the AI you trust most for the job.

**Free accounts are enough:** Claude Free, ChatGPT Free and Gemini (personal Google account) all work. No API keys and no paid plan needed. If you already use the **Claude Code CLI**, SnapAsk detects it and uses it for Claude automatically.

## Features

- **Global shortcut**: **fn + ⌃ + S** on Mac and **Ctrl + Alt + S** on Windows by default (both changeable).
- **Region capture**: the native selector on macOS (press **Space** to grab a whole window) and a dimmed drag-to-select overlay on Windows.
- **Popup** next to where you made the snapshot. It's an ordinary window, so your work can cover it. Close it with **Esc**.
- **Bring-back shortcut**: **fn + ⌃ + A** (Mac) or **Ctrl + Alt + A** (Windows) brings the last popup back in front of your work, ready for the next question in the same chat. Press it again to send the popup back. SnapAsk also shows in the Dock / ⌘Tab (Mac) or the taskbar / Alt+Tab (Windows) while a popup is open.
- **Three AIs, one box:** **Claude**, **ChatGPT** and **Gemini**. Switch with ⌘1/⌘2/⌘3 (Mac) or Ctrl+1/2/3 (Windows).
- **Your own accounts, no API keys:** each AI runs through its real website (claude.ai, chatgpt.com, gemini.google.com), loaded in a hidden page in the background and signed in to your account. This works on free plans, and with school or work accounts that block third-party tools.
  - **Claude Code CLI users:** if `claude` is installed and signed in, SnapAsk uses it for Claude automatically. It's slightly faster, and you don't need to sign in again. You can also force either route.
- **Streaming answers** with bold, bullet lists and code.
- **Follow-up questions** keep the conversation going. If you switch AIs midway, the new one gets the snapshot and the conversation so far.
- **More images in follow-ups**: paste one (**⌘V** / **Ctrl+V**, e.g. a screenshot you copied to the clipboard), drop an image file on the popup, or click the camera button to snap another area. It goes with your next message.
- **Continue a saved chat**: **Chats ▾** lists your recent conversations on the selected AI's site. Pick one and your next question (with the snapshot) goes into that chat instead of a new one. **New chat** starts over.
- **Open full chat ↗** shows the real website for that conversation.
- **Setup window** with sign-in status for each AI and one-click **Sign in** buttons.
- Lives in the **menu bar** (Mac) or **system tray** (Windows). Optional start at login.

## Download

Prebuilt downloads are on the **[Releases page](https://github.com/houtsing/SnapAsk-AI/releases/latest)**:

| Platform | File | Notes |
|---|---|---|
| **Windows 10/11** (x64) | `SnapAsk-Windows-x64.zip` | Unzip and run `SnapAsk.exe`. Nothing else to install. |
| **macOS 14+** (Apple Silicon) | `SnapAsk-macOS-arm64.zip` | Unzip, move `SnapAsk.app` to Applications, then **right-click → Open** the first time (it isn't notarised by Apple). |

Or build from source, below.

---

## Signing in to Claude, ChatGPT and Gemini

SnapAsk never sees or stores your password. It shows each AI's **own login page** in a small built-in browser window, and you sign in there exactly as you would in a normal browser (email, Google, Apple, Microsoft, or school/work single sign-on). The site's cookies are then kept in SnapAsk's private browser storage on your computer, so you stay signed in, just like a browser.

1. On first launch, the **SnapAsk setup** window opens, with an **Accounts** list:

   | | |
   |---|---|
   | ✔ Claude | Signed in / *via Claude Code CLI* |
   | ! ChatGPT | Not signed in → **Sign in…** |
   | ✔ Gemini | Signed in |

2. Click **Sign in…** next to each AI you want to use. The site's login page opens. Sign in, then close that window. The status turns ✔ within a few seconds.
3. You only need the AIs you plan to use. Free accounts are fine.

You can reopen this window any time: **menu bar icon → Setup & Accounts…** (Mac) or **tray icon → Setup & accounts…** (Windows). If you ask an AI you haven't signed in to, the popup shows a **Sign in to …** button right there.

### Claude: claude.ai or the Claude Code CLI

The setup window has a **Claude via** option:

| Option | What it does |
|---|---|
| **Auto** (default) | Uses the Claude Code CLI if it's installed and signed in (`claude auth status`), otherwise claude.ai |
| **claude.ai** | Always uses the website (works with any plan, including Free) |
| **Claude Code CLI** | Always uses the `claude` command (Pro/Max or API account; slightly faster, streams token by token) |

If the CLI isn't signed in yet, the **Sign in…** button runs `claude auth login` for you.

To sign out, open the AI with **Open** in the setup window and sign out on the site as usual. To remove everything, uninstall (see below).

---

## macOS

### Requirements

- macOS 14 (Sonoma) or later, on Apple Silicon.
- To build from source: **Xcode Command Line Tools** (`xcode-select --install`).

### Build and install from source

```bash
git clone https://github.com/houtsing/SnapAsk-AI.git
cd SnapAsk-AI/macos

# (Recommended) a local signing certificate, so macOS remembers SnapAsk's permissions across rebuilds
./scripts/create-signing-cert.sh

# Build and install to ~/Applications/SnapAsk.app
./build.sh
open ~/Applications/SnapAsk.app
```

No git? Click **Code → Download ZIP** on GitHub, unzip it, and run the same commands from the `macos` folder.

### First launch: permissions

SnapAsk opens a small setup window showing what still needs permission:

| Permission | Why | Where |
|---|---|---|
| **Screen & System Audio Recording** | To capture the region you select | System Settings → Privacy & Security → Screen & System Audio Recording |
| **Accessibility** | Only for the **fn + ⌃ + S** shortcut (fn can't be a normal macOS hotkey modifier) | System Settings → Privacy & Security → Accessibility |

After turning on Screen Recording, **quit and reopen SnapAsk** (menu bar icon → Quit). If SnapAsk is missing from a list, drag `SnapAsk.app` into it.

### Sign in

The setup window opens on first launch. See [Signing in](#signing-in-to-claude-chatgpt-and-gemini).

### Using it

1. Press **fn + ⌃ + S** (or your chosen shortcut) and drag over an area.
2. Type your question and press **Return**. Ask follow-ups, switch AIs with **⌘1 / ⌘2 / ⌘3**, and close with **Esc**.
3. Go back to work. Other windows can cover the popup. When you want to ask more, press **fn + ⌃ + A** to bring it back to the same chat. Press it again to hide it.

| Key | Action |
|---|---|
| **fn + ⌃ + S** | Snap & ask |
| **fn + ⌃ + A** | Bring back the last popup / send it back |
| **Return** | Send |
| **⌘V** | Paste an image into the next message (text pastes as usual) |
| **⌘1 / ⌘2 / ⌘3** | Switch to Claude / ChatGPT / Gemini |
| **⌘M** / **⌘W** or **Esc** | Minimise / close the popup |

You can change both shortcuts in **Setup & Accounts** (the fn shortcuts need the Accessibility permission). Menu bar icon: Snap & Ask, Bring Back Popup, setup, default AI, sign in, Launch at Login, Quit.

### Options

```bash
defaults write com.snapask.app claude.backend web    # same as "Claude via" in the setup window: auto | web | cli
defaults write com.snapask.app model.claude opus     # model for the CLI route (optional)
```

### Where SnapAsk stores things

| What | Path |
|---|---|
| App | `~/Applications/SnapAsk.app` |
| Settings | `~/Library/Preferences/com.snapask.app.plist` |
| Website sign-ins (cookies) | `~/Library/WebKit/com.snapask.app/`, `~/Library/HTTPStorages/com.snapask.app.binarycookies` |
| Temporary snapshots (deleted when the popup closes) | `~/Library/Caches/SnapAsk/` |
| Log | `~/Library/Logs/SnapAsk.log` |

### Uninstall

```bash
./macos/scripts/uninstall.sh
```

---

## Windows

### Requirements

- Windows 10 or 11 (x64).
- **Microsoft Edge WebView2 Runtime**, already built into Windows 11 and up-to-date Windows 10. If it's missing, get it [from Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/).
- To build from source: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### Build from source

```powershell
git clone https://github.com/houtsing/SnapAsk-AI.git
cd SnapAsk-AI\windows\SnapAsk
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
.\out\SnapAsk.exe
```

### First launch

SnapAsk appears as a **viewfinder icon in the system tray** (bottom-right; it may be under the **^** arrow). No special permissions are needed.

1. The **SnapAsk setup** window opens. Click **Sign in…** next to each AI you want ([details](#signing-in-to-claude-chatgpt-and-gemini)).
2. Press **Ctrl + Alt + S**, drag over an area, and type your question in the popup.

| Key | Action |
|---|---|
| **Enter** / **Shift + Enter** | Send / new line |
| **Ctrl + Alt + A** | Bring back the last popup (press again to minimise it) |
| **Ctrl + V** | Paste an image into the next message (text pastes as usual) |
| **Ctrl + 1 / 2 / 3** | Switch to Claude / ChatGPT / Gemini |
| **Esc** | Close the popup (or cancel a selection) |

Tray menu: Snap & Ask, Bring back popup, Setup & accounts, default AI, **Shortcut** (Ctrl+Alt+S, Ctrl+Shift+Space, Alt+Shift+A, Ctrl+Shift+2, Ctrl+Alt+Shift+S), **Bring-back shortcut** (Ctrl+Alt+A, Ctrl+Alt+R, Ctrl+Shift+1, Alt+Shift+S, Off), sign in, **Start with Windows**, Quit. Double-click the tray icon to snap.

### Where SnapAsk stores things

| What | Path |
|---|---|
| Settings (default AI, shortcut, Claude route) | `%APPDATA%\SnapAsk\settings.json` |
| Website sign-ins, log | `%LOCALAPPDATA%\SnapAsk\` |

**Uninstall:** if you turned on *Start with Windows*, turn it off from the tray menu first. Then quit SnapAsk and delete `SnapAsk.exe` and the two folders above.

---

## How it works

```
 shortcut ──▶ region capture ──▶ PNG ──▶ floating popup
                                            │
                    ┌───────────────────────┼───────────────────────┐
                    ▼                       ▼                       ▼
                 claude.ai              chatgpt.com          gemini.google.com
        (hidden, pre-loaded web pages signed in with your account: the popup attaches
         the image, types your question, presses send, then streams the reply back)
```

| | macOS | Windows |
|---|---|---|
| Language / UI | Swift, AppKit + SwiftUI (one file, built with `swiftc`) | C# / .NET 8, WPF + WinForms |
| Shortcut | Carbon `RegisterEventHotKey`; `CGEventTap` for fn + ⌃ + S | `RegisterHotKey` |
| Capture | `/usr/sbin/screencapture -i` | Full-desktop grab + custom drag-select overlay |
| Hidden pages | `WKWebView` in an off-screen window (occlusion detection off) | WebView2 in an off-screen window (background throttling off) |

- Each site loads once at launch, so asking is immediate. A question attaches the PNG to the site's own box (synthetic paste, then the page's file input, then drag-and-drop as fallbacks), types the text, clicks send, then reads the newest reply every 250 ms and turns it into lightweight markdown (bold, lists, code, LaTeX).
- Each new snapshot starts a fresh chat, unless you pick a saved one from **Chats ▾** (read from the site's own history sidebar). Follow-ups stay in that chat. When you switch AIs midway, the earlier turns go along with the next question.
- The popup remembers its chat's address on each site. If another popup uses the same hidden page in between, the next follow-up reopens your chat first, so nothing is lost or replayed.
- The page-automation scripts are in `windows/SnapAsk/Scripts/` (and inline in `macos/SnapAsk.swift`). The CSS selectors for each site are at the top of `macos/SnapAsk.swift` and in `windows/SnapAsk/Providers.cs`.

## Project layout

```
macos/
  SnapAsk.swift                   the whole Mac app
  build.sh                        compile, bundle, sign, install to ~/Applications
  scripts/create-signing-cert.sh  one-time local code-signing certificate
  scripts/uninstall.sh            remove the app and all its data
windows/SnapAsk/
  Program.cs        tray icon, menu, startup
  Accounts.cs       setup window: sign-in status, Sign in buttons, Claude route
  ClaudeCli.cs      Claude Code CLI route (detection + streaming session)
  HotKeys.cs        global shortcuts (snap, bring back)
  Capture.cs        drag-to-select screen overlay
  Popup.cs          the ask window (chats menu, image paste, bring-back)
  WebBackend.cs     hidden WebView2 pages that talk to each site
  Providers.cs      per-site CSS selectors + system prompt
  Markdown.cs       reply formatting
  Scripts/*.js      page automation (attach, send, read reply)
.github/workflows/build.yml   builds both apps; tagging vX.Y.Z publishes a release
docs/screenshot.png
```

## Troubleshooting

| Problem | Fix |
|---|---|
| Mac: shortcut does nothing | Reopen the app to see the setup window. Both permissions must be green. Try another shortcut there. |
| Mac: permissions say "not granted" even though they're on | The entry belongs to an older build. Remove SnapAsk from the list with **–** and add it again (or run `tccutil reset ScreenCapture com.snapask.app` and `tccutil reset Accessibility com.snapask.app`). `create-signing-cert.sh` prevents this. |
| Windows: shortcut does nothing | Another app owns it. Pick a different one in tray menu → Shortcut. |
| "… isn't signed in" | Click the **Sign in to …** button in the popup, or use the setup window. |
| Google sign-in says "this browser may not be secure" | Use the site's email sign-in option instead, or sign in with Apple or Microsoft. |
| An AI stops answering or times out | The site probably changed its page layout, so the selectors need updating (see How it works). Open an issue, or use **Open full chat ↗** in the meantime. Logs: `~/Library/Logs/SnapAsk.log` (Mac), `%LOCALAPPDATA%\SnapAsk\SnapAsk.log` (Windows). |

## Limitations and notes

- Mac builds target Apple Silicon (edit `-target` in `macos/build.sh` for Intel). Windows builds target x64.
- The AIs are driven through their web interfaces, which change often. Expect occasional fixes to the selectors.
- Free plans have usage limits set by each provider. When you hit one, the site's own message appears in the popup.
- Conversations appear in your normal chat history on each site.
- You're responsible for following each provider's terms of use.
- For development, the Mac app listens for a few local `com.snapask.debug.*` notifications that open, query and screenshot a popup without a person at the keyboard. They're only reachable by processes on your own Mac.
