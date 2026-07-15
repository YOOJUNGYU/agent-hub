# Agent Hub

[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)

[한국어](README.md) | **English**

**A program that lets you watch and control the AI agents (e.g. Claude) running on your PC — from your phone, even while you're away.**

> ### 🔐 Within your trusted local network, only devices you approve
> Step away and still use a phone on the same network to **answer Claude's questions, approve risky actions, and send commands**.
> **Monitoring and control traffic stays inside your home/office network (LAN)** (only closed‑app push alerts pass through an external, end‑to‑end‑encrypted push service), and **you decide which devices can connect and control — right from the PC**.

---

## 📖 User Guide

We built a **visual, follow-along guide** covering everything: install → certificate → app install (Add to Home Screen) → enabling alerts.

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

1. Check the **access address** at the top of the PC console. (e.g. `https://192.168.0.10:47600`)
2. Put your phone on the **same network** as the PC and open that address in its browser.
3. **Request authorization** → **Approve** it in the PC console, and the monitor screen appears.

> 📱 To receive **alerts**, you also need to **install the certificate and Add to Home Screen (install the app)**. For the detailed, illustrated steps, see the **[User Guide](https://yoojungyu.github.io/agent-hub/)**.

---

## Key Features

- **Real-time monitoring** — session status (working / idle / ended), current task, elapsed time, and cumulative tokens.
- **Control from your phone** — answer questions, allow/deny risky actions, send commands. (Optional: a session terminal for prompts and /slash commands.)
- **Alerts even when the app is closed** — a push arrives when a session needs you, carrying Claude's **last message** as the body.
- **Device authorization** — only approved devices can connect; approve, revoke, or delete them from the console.
- **Tray resident · auto-update** — stays quietly in the tray; new versions apply automatically.

---

## How is this different from Claude Code's official Remote Control?

Claude Code has an official **Remote Control** feature for taking over a session remotely. The goal (control Claude from your phone while you're away) is similar, but Agent Hub differs fundamentally in **how it connects and what it assumes.**

| | **Claude Code Remote Control (official)** | **Agent Hub** |
|---|---|---|
| **Connection path** | Via Anthropic cloud (API) | **Local server on your LAN only** — control & monitoring never leave |
| **Reach** | Anywhere over the internet | **Same network (home/office LAN)** |
| **Requirements** | Pro/Max/Team/Enterprise plan + account login | **No plan needed** — just local Claude Code |
| **Access control** | Account/org policy, trusted devices | **You approve each device on your PC** |
| **Sessions** | A session you start in remote-control mode | **Auto-detects sessions already running locally** |
| **Clients** | Native mobile app · web | **PWA (home-screen app)** + PC console |

> 💡 In short: pick Agent Hub when you want **no cloud relay — inside your own network, only devices you approved.** (Only closed-app push alerts pass through an external, encrypted push service.)

---

## Settings · Update

- In the console **Settings** tab, change the server **port** (default `47600`) and **display language** (Korean / English).
- New versions download automatically and **apply on the next launch**. (Or tray right-click → *Update now and restart* to apply immediately.)

---

## Code signing

Code signing for the Windows installer is provided free of charge to open source projects by [SignPath.io](https://signpath.io), with a certificate issued by the [SignPath Foundation](https://signpath.org).

---

## Contact

If you run into a problem or have a suggestion, please open an [issue](https://github.com/YOOJUNGYU/agent-hub/issues).
