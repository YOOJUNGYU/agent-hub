# Agent Hub

[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)

[한국어](README.md) | **English**

**A Windows P2P remote-control tool for monitoring Claude Code and Codex CLI sessions from your phone, answering questions, approving permissions, and sending commands.**

> ### 🔐 Device‑to‑device with no cloud relay, only devices you approve
> Step away and still use your phone to **answer Claude/Codex questions, approve risky actions, and send commands**.
> A P2P VPN like **NetBird** links your PC and phone directly, so you can reach it **from anywhere** — home, office, cafe, or mobile data (no VPN needed if both are on the same network) — and monitoring/control traffic goes **device‑to‑device with no cloud relay** (only closed‑app push alerts pass through an external, end‑to‑end‑encrypted push service). **You decide which devices can connect and control — right from the PC.**

---

## 📖 User Guide

We built a **visual, follow-along guide** covering everything: connect with NetBird → certificate → app install (Add to Home Screen) → device approval → remote control.

### 👉 **[Agent Hub User Guide - Claude Code and Codex mobile remote control](https://yoojungyu.github.io/agent-hub/)**

---

## Download & Install

1. Go to the **[latest release](https://github.com/YOOJUNGYU/agent-hub/releases/latest)** page and download **`AgentHub-win-Setup.exe`**.
2. **Double-click** the file to install. (No administrator rights required.)
3. On first launch, if a certificate install dialog appears, click **Yes**.

> 💡 If a blue **"Windows protected your PC"** warning appears, click **More info → Run anyway**. (Shown because the program is unsigned.)

Agent Hub is a **background tray app with no window of its own**.

- Launch it from the **Start menu or desktop shortcut** and the screen **opens in your default browser.** Launching it again while it's running just opens the screen.
- When it **starts automatically at Windows login**, it stays **quietly in the tray** without opening the screen.
- Open the installed app (PWA) on the PC while the server is off and it **starts the server for you** (`agenthub://` — the first time the browser asks to open it).
- Tray menu: **열기 (Open) · 업데이트 (Update) · 완전 종료 (Quit)**
- The screen is a **PWA used the same way on PC and phone**. On the PC, open `https://127.0.0.1:<port>/host` in Edge/Chrome (the address the tray **Open** item opens) and click the **install icon in the address bar → Install** to use it like an app. HTTPS opens without warnings because Agent Hub registers its own root certificate in the Windows user trust store at startup.

---

## Quick Start (connect a phone)

1. Open the screen on the PC (run the shortcut or use the tray **Open** item). The top bar shows the **access address**: the local‑network (LAN) address and the VPN (e.g. NetBird) address are listed **each with its own label**. (e.g. `LAN https://192.168.0.10:47600`, `VPN https://100.x.x.x:47600`)
2. **Install the certificate** on the phone (`/api/cert`, also available via the top bar's **▣ QR**) and open that address in the phone's browser: use the **LAN** address when both are on the **same network**, or install NetBird on **both PC and phone**, sign in, and use the **VPN** address to reach it from anywhere.
3. **Request authorization** → **Approve** it under **Devices** in the PC screen's left menu, and the phone shows the same **office** screen as the PC. (Phones see only the office; device approval, logs, and settings are PC‑only.)
4. To get alerts, tap **🔔 알림 켜기 (Enable alerts)** in the phone's top bar.

> 📱 To receive **alerts**, you also need to **install the certificate and Add to Home Screen (install the app)**. For the detailed, illustrated steps, see the **[User Guide](https://yoojungyu.github.io/agent-hub/)**.

---

## Key Features

- **The office** — every Claude Code / Codex session on Windows and WSL appears as a character (cats and dogs in hoodies) in the office (pastel rooms drawn as smooth vector graphics, light/dark theme) and moves between rooms as it works: 📚 **Library** (reading files, searching, web fetch/search — a reference card shows the exact file path, URL, or query) · 🗂 **Office** (thinking/writing between tools, todo lists, delegating to sub‑agents) · 🔧 **Workshop** (editing code, running commands) · 🔔 **Consult room** (waiting for you on a question or permission prompt) · ☕ **Lounge** (resting between turns — quiet, label on hover). 💭 thinking‑summary and 💬 explanation bubbles plus labels with the project name and where it runs (· Codex, · WSL). **Drag a room by its name plate** to rearrange and **by its bottom-right corner** to enlarge it (saved per browser), pick the floor (solid, wood or tile, in several colours) with the 🎨 button; hover a room to see its role and who's inside. Wheel to zoom, drag to pan (pinch/drag on phones). Sub‑agents and background shells appear as small characters tied to their main agent by a heart string (holding hands when close). **Only sessions that can receive prompts** are shown (exited ones disappear), and the list updates incrementally so the office appears within seconds.
- **Chat panel** — click a character, its thought bubble or label, or a roster card on the right (phones too) to open a panel with **대화 (Chat) | 터미널 (Terminal)** tabs — live activity feed (**bold** and `code` rendered), question cards (multiple questions, Other — type your own), permission allow/deny, and prompt input. On the PC, drag its left edge to resize. Questions and permission prompts also pop a toast in the office and a Windows tray notification; clicking it opens that session's chat.
- **Launch a new agent** — the **＋ card** under the right roster (on phones, the **＋** in the bottom bar): pick the engine (Claude Code / Codex), where to run (Windows or an installed WSL distro), the working folder (recent folders suggested), and an optional first prompt, and it runs hidden as a child process of AgentHub (ConPTY) — no console window; view and type into it in the chat panel's **터미널 (Terminal)** tab. Quitting AgentHub ends these agents. Works from the phone too (approved devices). Requires the CLI (`claude` / `codex`) in that environment.
- **One screen · left menu** — Office · Devices (device approval + connected devices) · Logs · Settings. A one‑line top bar with server status, LAN/VPN addresses, QR, how‑to link, language, and a ☀️/🌙 theme toggle. Light is the default; the chosen theme is shared by the console, office, and chat panel.
- **Reachable from anywhere** — a P2P VPN like NetBird links your PC and phone directly. The certificate is issued **covering the VPN IP range (`100.64.0.0/10`)**, and the console lists the LAN and VPN addresses separately.
- **Control from your phone** — answer questions, allow/deny risky actions, send commands. (Optional: a session terminal for prompts and /slash commands.)
- **Permission approvals that know you're away** — while you're away (no keyboard/mouse input for a minute) permission requests are **held for up to ~10 minutes** so you can allow/deny from the phone; when you're at the PC the prompt appears **in the PC terminal right away**. Requests whose window expired stay as a **pending‑permission card** you can still answer from the phone.
- **Alerts even when the app is closed** — a push arrives when a session needs you, carrying the AI session's **last message** as the body.
- **WSL sessions too** — Claude/Codex CLI sessions started inside WSL (Ubuntu, etc.) are detected automatically (Codex sessions using the newer exec/apply_patch format show their tool activity too), and alerts, question answers, permission approvals, and command sending all work just like Windows sessions.
- **Device authorization** — only approved devices can connect; approve, revoke, or delete them from the console.
- **Tray resident · auto-update** — no window, stays quietly in the tray (menu: Open · Update · Quit); new versions apply automatically.

---

## How is this different from Claude Code's official Remote Control?

Claude Code has an official **Remote Control** feature for taking over a session remotely. The goal (control Claude from your phone while you're away) is similar, but Agent Hub differs fundamentally in **how it connects and what it assumes.**

| | **Claude Code Remote Control (official)** | **Agent Hub** |
|---|---|---|
| **Connection path** | Via Anthropic cloud (API) | **Device‑to‑device (P2P), no cloud** — control & monitoring never pass through our cloud |
| **Reach** | Anywhere over the internet | **Anywhere via a VPN like NetBird** (device‑to‑device) · no VPN needed on the same network |
| **Requirements** | Pro/Max/Team/Enterprise plan + account login | **No plan needed** — just local Claude Code |
| **Access control** | Account/org policy, trusted devices | **You approve each device on your PC** |
| **Sessions** | A session you start in remote-control mode | **Auto-detects sessions already running locally** |
| **Clients** | Native mobile app · web | **PWA (installable app)** — the same screen on PC and phone |

> 💡 In short: pick Agent Hub when you want **no cloud relay — device‑to‑device, only devices you approved.** Anywhere via a VPN like NetBird, on wired or Wi‑Fi. (Only closed-app push alerts pass through an external, encrypted push service.)

---

## Settings · Update

- Under **Settings** in the PC screen's left menu, change the server **port** (default `47600`) and **display language** (Korean / English).
- New versions download automatically and **apply on the next launch**. (Or tray menu **Update** → *Update now and restart* to apply immediately.)

---

## Credits

- The office is based on the MIT‑licensed [pixel-agents](https://github.com/pixel-agents-hq/pixel-agents) project. The characters, furniture, and tiles are original Agent Hub art — drawn as vectors by `office/webview-ui/src/hub/vectorScene.ts`; the room kit and furniture catalog are generated by `office/art`.

---

## Contact

If you run into a problem or have a suggestion, please open an [issue](https://github.com/YOOJUNGYU/agent-hub/issues).
