# Agent Hub

[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)

[한국어](README.md) | **English**

**A program that lets you watch and control the AI agents (Claude and Codex) running on your PC — from your phone, even while you're away.**

> ### 🔐 Device‑to‑device with no cloud relay, only devices you approve
> Step away and still use your phone to **answer Claude/Codex questions, approve risky actions, and send commands**.
> A P2P VPN like **NetBird** links your PC and phone directly, so you can reach it **from anywhere** — home, office, cafe, or mobile data (no VPN needed if both are on the same network) — and monitoring/control traffic goes **device‑to‑device with no cloud relay** (only closed‑app push alerts pass through an external, end‑to‑end‑encrypted push service). **You decide which devices can connect and control — right from the PC.**

---

## 📖 User Guide

We built a **visual, follow-along guide** covering everything: connect with NetBird → certificate → app install (Add to Home Screen) → device approval → remote control.

### 👉 **[Open the User Guide](https://yoojungyu.github.io/agent-hub/)**

---

## Download & Install

1. Go to the **[latest release](https://github.com/YOOJUNGYU/agent-hub/releases/latest)** page and download **`AgentHub-win-Setup.exe`**.
2. **Double-click** the file to install. (No administrator rights required.)
3. On first launch, if a certificate install dialog appears, click **Yes**.

> 💡 If a blue **"Windows protected your PC"** warning appears, click **More info → Run anyway**. (Shown because the program is unsigned.)

After installation, the program stays in the **taskbar tray** (bottom-right).

---

## Quick Start (connect a phone)

1. Check the **access address** at the top of the PC console. The local‑network (LAN) address and the VPN (e.g. NetBird) address are listed **each with its own label**. (e.g. `LAN https://192.168.0.10:47600`, `VPN https://100.x.x.x:47600`)
2. Open that address in the phone's browser: use the **LAN** address when both are on the **same network**, or install NetBird on **both PC and phone**, sign in, and use the **VPN** address to reach it from anywhere.
3. **Request authorization** → **Approve** it in the PC console, and the monitor screen appears.

> 📱 To receive **alerts**, you also need to **install the certificate and Add to Home Screen (install the app)**. For the detailed, illustrated steps, see the **[User Guide](https://yoojungyu.github.io/agent-hub/)**.

---

## Key Features

- **Real-time monitoring** — session status (working / idle / ended), current task, elapsed time, and cumulative tokens.
- **Reachable from anywhere** — a P2P VPN like NetBird links your PC and phone directly. The certificate is issued **covering the VPN IP range (`100.64.0.0/10`)**, and the console lists the LAN and VPN addresses separately.
- **Control from your phone** — answer questions, allow/deny risky actions, send commands. (Optional: a session terminal for prompts and /slash commands.)
- **Permission approvals that know you're away** — while you're away (no keyboard/mouse input for a minute) permission requests are **held for up to ~10 minutes** so you can allow/deny from the phone; when you're at the PC the prompt appears **in the PC terminal right away**. Requests whose window expired stay as a **pending‑permission card** you can still answer from the phone.
- **Alerts even when the app is closed** — a push arrives when a session needs you, carrying the AI session's **last message** as the body.
- **WSL sessions too** — Claude/Codex CLI sessions started inside WSL (Ubuntu, etc.) are detected automatically, and alerts, question answers, permission approvals, and command sending all work just like Windows sessions.
- **Device authorization** — only approved devices can connect; approve, revoke, or delete them from the console.
- **Tray resident · auto-update** — stays quietly in the tray; new versions apply automatically.

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
| **Clients** | Native mobile app · web | **PWA (home-screen app)** + PC console |

> 💡 In short: pick Agent Hub when you want **no cloud relay — device‑to‑device, only devices you approved.** Anywhere via a VPN like NetBird, on wired or Wi‑Fi. (Only closed-app push alerts pass through an external, encrypted push service.)

---

## Settings · Update

- In the console **Settings** tab, change the server **port** (default `47600`) and **display language** (Korean / English).
- New versions download automatically and **apply on the next launch**. (Or tray right-click → *Update now and restart* to apply immediately.)

---


## Contact

If you run into a problem or have a suggestion, please open an [issue](https://github.com/YOOJUNGYU/agent-hub/issues).
