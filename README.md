# SnapAsk AI

**Snapshot any part of your screen and ask Claude, ChatGPT or Gemini about it in one step, from any app.**

Press a shortcut, drag over what you want to ask about (a lecture slide, a chart, an error message, a paragraph in a PDF), and a small popup appears right there, a bit like macOS's *Look Up / Define*. Type your question and the answer appears in the popup. Ask follow-ups, or switch to a different AI mid-conversation.

![SnapAsk popup](docs/screenshot.png)

---

## Why

Asking an AI about something on screen normally takes several steps: take a screenshot, open the AI's website, paste it in, type the question, then switch back. Chrome's built-in "Ask Gemini" helps, but it only works in Chrome and only with Gemini.

SnapAsk turns that into **one shortcut**. It works in any app (Preview, PowerPoint, Zoom, a terminal, anything), with the AI you trust most for the job.

## Features

- **Global shortcut**: **fn + ⌃ + S** by default (or ⌘⇧2, ⌃⇧Space, ⌥⇧A, ⌘⌥A, ⌃⌥⌘S).
- **Region capture** with the native macOS selector. Press **Space** while selecting to grab a whole window.
- **Floating popup** next to where you made the snapshot. It stays on top until you close it (**Esc**).
- **Three AIs, one box:** **Claude**, **ChatGPT** and **Gemini**. Switch with ⌘1 / ⌘2 / ⌘3.
- **Your own accounts, no API keys:**
  - Claude runs through the official Claude Code CLI, signed in to your Claude subscription.
  - ChatGPT and Gemini run through their real websites (chatgpt.com / gemini.google.com), loaded in hidden pages in the background and signed in to your account. This also works with school or work accounts that block third-party tools.
- **Streaming answers** with bold, bullet lists and code.
- **Follow-up questions** keep the conversation going. If you switch AIs midway, the new one gets the snapshot and the conversation so far.
- **Open full chat ↗** shows the real ChatGPT or Gemini page for that conversation.
- Lives in the **menu bar**, with no Dock icon. Optional **Launch at Login**.

## Requirements

- macOS 14 (Sonoma) or later, on Apple Silicon (arm64).
- **Xcode Command Line Tools**, to build: `xcode-select --install`.
- For **Claude**: the [Claude Code CLI](https://claude.com/claude-code), signed in with your Claude account (run `claude` once in Terminal).
- For **ChatGPT / Gemini**: just a normal account on chatgpt.com / gemini.google.com.

## Download and install

```bash
# 1. Download the code
git clone https://github.com/<your-username>/SnapAsk-AI.git
cd SnapAsk-AI

# 2. (Recommended) create a local signing certificate so macOS remembers
#    SnapAsk's permissions across rebuilds
./scripts/create-signing-cert.sh

# 3. Build and install to ~/Applications/SnapAsk.app
./build.sh

# 4. Launch it
open ~/Applications/SnapAsk.app
```

No git? Click **Code → Download ZIP** on GitHub, unzip it, then run steps 2 to 4 from that folder in Terminal.

### First launch: permissions

SnapAsk opens a small setup window showing what still needs permission:

| Permission | Why | Where |
|---|---|---|
| **Screen & System Audio Recording** | To capture the region you select | System Settings → Privacy & Security → Screen & System Audio Recording |
| **Accessibility** | Only for the **fn + ⌃ + S** shortcut (fn can't be a normal macOS hotkey modifier) | System Settings → Privacy & Security → Accessibility |

After turning on Screen Recording, **quit and reopen SnapAsk** (menu bar icon → Quit). If SnapAsk is missing from a list, drag `~/Applications/SnapAsk.app` into it.

### Sign in to ChatGPT and Gemini

Menu bar icon → **Sign in** → **ChatGPT** / **Gemini**. A small browser window opens. Sign in as you normally would (Google, Microsoft, Apple, email or school/work SSO), then close the window. You stay signed in.

## Usage

1. Press **fn + ⌃ + S** (or your chosen shortcut).
2. Drag over the area you want to ask about.
3. Type your question in the popup and press **Return**.
4. Ask follow-ups, switch AIs with **⌘1 / ⌘2 / ⌘3**, and close with **Esc**.

| Where | What you can do |
|---|---|
| Popup thumbnail | Click to open the snapshot in Preview |
| Menu bar icon | Snap & Ask, change the shortcut, check permissions, choose the default AI, sign in, Launch at Login, Quit |
| Reopen the app | Opening SnapAsk again shows the setup window |

**Optional:** choose a specific Claude model with:

```bash
defaults write com.snapask.app model.claude opus     # or sonnet, haiku, a full model id…
```

## How it works

```
 fn+⌃S ──▶ /usr/sbin/screencapture -i ──▶ snapshot.png ──▶ floating popup (SwiftUI)
                                                              │
               ┌──────────────────────────────────────────────┼─────────────────────────────┐
               ▼                                              ▼                             ▼
            Claude                                        ChatGPT                        Gemini
  `claude -p` (stream-json in/out),          hidden WKWebView on chatgpt.com   hidden WKWebView on gemini.google.com
  image sent inline, one long-lived          JS attaches the image, types,     (same approach)
  process per popup → streamed tokens        presses send, reads the reply
```

- **One Swift file** (`SnapAsk.swift`), built with `swiftc`. No Xcode project, no dependencies.
- **Shortcut:** Carbon `RegisterEventHotKey` for normal shortcuts; a `CGEventTap` for fn + ⌃ + S, which is consumed so ⌃S doesn't also reach the app you're in.
- **Claude:** `claude -p --input-format stream-json --output-format stream-json --include-partial-messages`, with tools, MCP servers, hooks and settings turned off, so it runs as a plain fast chat. The process stays alive for follow-ups.
- **ChatGPT / Gemini:** each site loads once at launch in an off-screen `WKWebView` (with WebKit's window-occlusion detection turned off so the page keeps rendering). A question attaches the PNG to the site's own box, types the text, clicks send, then reads the newest reply every 250 ms and turns it into lightweight markdown. Each new snapshot starts a fresh chat.
- **Context hand-off:** when you switch AIs mid-conversation, the earlier turns are sent along with the next question.

### Where SnapAsk stores things

| What | Path |
|---|---|
| App | `~/Applications/SnapAsk.app` |
| Settings | `~/Library/Preferences/com.snapask.app.plist` |
| ChatGPT / Gemini sign-in cookies | `~/Library/WebKit/com.snapask.app/`, `~/Library/HTTPStorages/com.snapask.app.binarycookies` |
| Temporary snapshots (deleted when the popup closes) | `~/Library/Caches/SnapAsk/` |
| Log | `~/Library/Logs/SnapAsk.log` |

## Project layout

```
SnapAsk.swift                   the whole app
build.sh                        compile, bundle, sign, install to ~/Applications
scripts/create-signing-cert.sh  one-time local code-signing certificate
scripts/uninstall.sh            remove the app and all its data
docs/screenshot.png
```

## Troubleshooting

| Problem | Fix |
|---|---|
| Shortcut does nothing | Check the setup window (reopen the app). Both permissions must be green. Try another shortcut there. |
| Permissions say "not granted" even though they're on | The entry belongs to an older build. Remove SnapAsk from the list with **–**, then add it again (or run `tccutil reset ScreenCapture com.snapask.app` and `tccutil reset Accessibility com.snapask.app`). Using `create-signing-cert.sh` prevents this. |
| "ChatGPT isn't signed in" | Menu bar → Sign in → ChatGPT. |
| ChatGPT / Gemini stops answering or times out | The site probably changed its page layout. The selectors are in `Provider.site` near the top of `SnapAsk.swift`. Open an issue, or use **Open full chat ↗** in the meantime. |
| Claude error | Run `claude` in Terminal once to sign in. Check `~/Library/Logs/SnapAsk.log`. |

## Uninstall

```bash
./scripts/uninstall.sh
```

## Limitations and notes

- macOS only, Apple Silicon only (edit `-target` in `build.sh` for Intel).
- ChatGPT and Gemini are driven through their web interfaces, which change often. Expect occasional fixes to the selectors.
- Conversations made through ChatGPT and Gemini appear in your normal chat history on those sites.
- You're responsible for following each provider's terms of use.
- For development, the app listens for a few local `com.snapask.debug.*` notifications that open, query and screenshot a popup without a person at the keyboard. They're only reachable by processes on your own Mac.
