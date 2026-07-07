# Agent Hub

[![Downloads](https://img.shields.io/github/downloads/YOOJUNGYU/agent-hub/total)](https://github.com/YOOJUNGYU/agent-hub/releases)
[![Latest Release](https://img.shields.io/github/v/release/YOOJUNGYU/agent-hub)](https://github.com/YOOJUNGYU/agent-hub/releases/latest)

[한국어](README.md) | **English**

**A program that lets you see, on both your computer and your phone, what the AI agents (e.g. Claude) running on your PC are doing.**

When your PC is busy with AI work at the office or at home, you can step away and still check the progress from **a phone on the same Wi‑Fi**.

---

## Download & Install

1. Go to the **[👉 latest release](https://github.com/YOOJUNGYU/agent-hub/releases/latest)** page and download **`AgentHub-win-Setup.exe`**.
2. **Double‑click** the downloaded file to install. (No administrator rights required.)
3. On first launch, if a "Do you want to install this certificate?" dialog appears **once**, click **Yes**. (This lets your PC and phone connect securely.)

> 💡 When downloading or first running it, a blue **"Windows protected your PC"** warning may appear. This is shown because the program is unsigned — click **More info → Run anyway**.

After installation, the program stays in the **taskbar tray** (bottom‑right of the screen) as an icon.

---

## How to Use

### Viewing on your computer
- **Double‑click** the Agent Hub tray icon to open the console window.
- The window shows server status (🟢 active), the access address, the list of connected phones, and logs.

### Viewing on your phone (same Wi‑Fi)
1. Check the **address shown at the top** of the computer console window (e.g. `https://192.168.0.10:47600`).
2. Type that address into your phone's browser. If a security warning appears, proceed with "Continue" / "Advanced → Proceed". (This is shown because your PC uses its own self‑signed certificate.)
3. On the first visit you'll see a **device authorization** screen. Enter a device name and tap **Request authorization**.
4. In the computer console's **Devices** tab (or the tray notification), review the request and **Approve** it — the **agent monitor screen** then appears automatically on the phone.
5. **Revoking** approval or **deleting** the device blocks that phone's access again.

> 📱 The first connection from a phone may fail. If so, you need to **allow inbound connections for that port in the PC's Windows Firewall**. (We plan to make this step easier.)

### Tray icon right‑click menu
- **Open** — opens the console window.
- **Update now and restart** — immediately applies a downloaded new version.
- **Exit completely** — fully quits the program. (The window's close [X] only hides it to the tray.)

---

## Key Features

- **Real‑time monitoring** — shows each AI agent's status (working / idle / error), current task, and progress in real time.
- **Mobile viewing** — check progress from anywhere using a phone browser on the same network.
- **Access address** — the console shows the address to open on your phone as a link.
- **Connected devices** — see which phones connected and when, from the console.
- **Device authorization** — only approved devices can access the monitor; approve, revoke, or delete devices from the console.
- **Tray resident** — stays quietly in the tray and opens a window only when needed.
- **Auto‑update** — new versions are downloaded automatically and applied on the next launch.

---

## Settings

In the console window's **Settings** tab you can change the server **port number** (default `47600` — change it here if it conflicts with another program) and the **display language** (Korean / English). Saving the port restarts the server at the new address. The language can also be changed from the 🌐 menu in the top‑right of the screen.

---

## Update

When a new version is released, it is downloaded automatically while the program is running, and a tray notification appears: **"A new version is ready. It will be applied on restart."** Relaunching the program runs the latest version. (You can also apply it immediately via tray right‑click → *Update now and restart*.)

---

## Contact

If you run into a problem or have a suggestion, please open an [issue](https://github.com/YOOJUNGYU/agent-hub/issues).
