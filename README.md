# SnapAsk AI

**Snapshot any part of your screen and ask Claude, ChatGPT or Gemini about it in one step, from any app, on macOS and Windows.**

Press a shortcut, drag over what you want to ask about (a lecture slide, a chart, an error message, a paragraph in a PDF), and a small popup appears right there, a bit like macOS's *Look Up / Define*. Type your question and the answer appears in the popup. Ask follow-ups, or switch to a different AI mid-conversation.

![SnapAsk popup](docs/screenshot.png)

---

## Why

Asking an AI about something on screen normally takes several steps: take a screenshot, open the AI's website, paste it in, type the question, then switch back. Chrome's built-in "Ask Gemini" helps, but it only works in Chrome and only with Gemini.

SnapAsk turns that into **one shortcut**. It works in any app (Preview, PowerPoint, Zoom, a terminal, anything), with the AI you trust most for the job.

**Free accounts are enough:** Claude Free, ChatGPT Free and Gemini (personal Google account) all work. No API keys and no paid plan needed.

## Features

- **Global shortcut**: **fn + ⌃ + S** on Mac and **Ctrl + Alt + S** on Windows by default (both changeable).
- **Region capture**: the native selector on macOS (press **Space** to grab a whole window) and a dimmed drag-to-select overlay on Windows.
- **Floating popup** next to where you made the snapshot. It stays on top until you close it (**Esc**).
- **Three AIs, one box:** **Claude**, **ChatGPT** and **Gemini**. Switch with ⌘1/⌘2/⌘3 (Mac) or Ctrl+1/2/3 (Windows).
- **Your own accounts, no API keys:** each AI runs through its real website (claude.ai, chatgpt.com, gemini.google.com), loaded in a hidden page in the background and signed in to your account. This works on free plans, and with school or work accounts that block third-party tools.
  - *Mac, optional:* if you have Claude Pro or Max and the Claude Code CLI, you can route Claude through the CLI instead for slightly faster replies (see [Options](#options)).
- **Streaming answers** with bold, bullet lists and code.
- **Follow-up questions** keep the conversation going. If you switch AIs midway, the new one gets the snapshot and the conversation so far.
- **Open full chat ↗** shows the real website for that conversation.
- Lives in the **menu bar** (Mac) or **system tray** (Windows). Optional start at login.

## Download

Prebuilt downloads are on the **[Releases page](https://github.com/houtsing/SnapAsk-AI/releases/latest)**:

| Platform | File | Notes |
|---|---|---|
| **Windows 10/11** (x64) | `SnapAsk-Windows-x64.zip` | Unzip and run `SnapAsk.exe`. Nothing else to install. |
| **macOS 14+** (Apple Silicon) | `SnapAsk-macOS-arm64.zip` | Unzip, move `SnapAsk.app` to Applications, then **right-click → Open** the first time (it isn't notarised by Apple). |

Or build from source, below.

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

Menu bar icon → **Sign in** → **Claude** / **ChatGPT** / **Gemini**. A small browser window opens. Sign in as you normally would, then close the window. You only need to sign in to the AIs you plan to use.

### Using it

1. Press **fn + ⌃ + S** (or your chosen shortcut) and drag over an area.
2. Type your question and press **Return**. Ask follow-ups, switch AIs with **⌘1 / ⌘2 / ⌘3**, and close with **Esc**.

Menu bar icon: Snap & Ask, shortcut and permissions, default AI, sign in, Launch at Login, Quit.

### Options

```bash
# Use the Claude Code CLI for Claude instead of claude.ai (needs Claude Pro/Max + `claude` signed in)
defaults write com.snapask.app claude.backend cli
defaults write com.snapask.app model.claude opus      # optional model for the CLI route
defaults delete com.snapask.app claude.backend        # back to claude.ai
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

1. Right-click the tray icon → **Sign in / open full chat** → **Claude** / **ChatGPT** / **Gemini**. Sign in, then close that window.
2. Press **Ctrl + Alt + S**, drag over an area, and type your question in the popup.

| Key | Action |
|---|---|
| **Enter** / **Shift + Enter** | Send / new line |
| **Ctrl + 1 / 2 / 3** | Switch to Claude / ChatGPT / Gemini |
| **Esc** | Close the popup (or cancel a selection) |

Tray menu: Snap & Ask, default AI, **Shortcut** (Ctrl+Alt+S, Ctrl+Shift+Space, Alt+Shift+A, Ctrl+Shift+2, Ctrl+Alt+Shift+S), sign in, **Start with Windows**, Quit. Double-click the tray icon to snap.

### Where SnapAsk stores things

| What | Path |
|---|---|
| Settings | `%APPDATA%\SnapAsk\settings.json` |
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
- Each new snapshot starts a fresh chat. Follow-ups stay in that chat. When you switch AIs midway, the earlier turns go along with the next question.
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
  HotKeys.cs        global shortcut
  Capture.cs        drag-to-select screen overlay
  Popup.cs          the floating ask window
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
| "… isn't signed in" | Mac: menu bar → Sign in. Windows: tray icon → Sign in / open full chat. |
| Google sign-in says "this browser may not be secure" | Use the site's email sign-in option instead, or sign in with Apple or Microsoft. |
| An AI stops answering or times out | The site probably changed its page layout, so the selectors need updating (see How it works). Open an issue, or use **Open full chat ↗** in the meantime. Logs: `~/Library/Logs/SnapAsk.log` (Mac), `%LOCALAPPDATA%\SnapAsk\SnapAsk.log` (Windows). |

## Limitations and notes

- Mac builds target Apple Silicon (edit `-target` in `macos/build.sh` for Intel). Windows builds target x64.
- The AIs are driven through their web interfaces, which change often. Expect occasional fixes to the selectors.
- Free plans have usage limits set by each provider. When you hit one, the site's own message appears in the popup.
- Conversations appear in your normal chat history on each site.
- You're responsible for following each provider's terms of use.
- For development, the Mac app listens for a few local `com.snapask.debug.*` notifications that open, query and screenshot a popup without a person at the keyboard. They're only reachable by processes on your own Mac.
