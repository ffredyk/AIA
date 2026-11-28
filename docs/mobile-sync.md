# AIA Mobile Sync

Two-way sync between AIA on Windows and the AIA Android app.

- Tasks: title, description, notes, status, priority, due/completed dates, subtasks, tags, dependencies, recurrence, archive state.
- Reminders: title, due date, severity, completed state.
- Calendar events: title, description, location, all-day, start/end, recurrence, reminder offset.

## How It Works

```
Android app (SQLite, offline-first)
  -> HTTPS + bearer token
  -> Cloudflare Tunnel / reverse proxy / VPN
  -> Kestrel inside AIA (loopback by default)
  -> revision journal -> tasks.json / reminders.json / calendar-events.json
```

The server uses a monotonic revision. The phone sends the revision each local edit is based on.
If the desktop version changed, the server wins and the phone replaces its copy.

The desktop app must be running for sync. There is no cloud account and no third-party calendar API.

## Requirements

Desktop:

- .NET 10.0 ASP.NET Core Runtime (or a self-contained AIA build).
- AIA Mobile sync enabled in **Settings → Mobile**.

Android build:

- .NET 10 SDK.
- `dotnet workload install maui-android`.
- JDK 17. Set `JAVA_HOME` and `AndroidSdkDirectory` if the SDK is not auto-detected.
- Android SDK platform 35/36 or newer.

Build:

```powershell
dotnet build Mobile\AIA.Mobile.csproj -f net10.0-android -c Release
```

APK output is under `Mobile\bin\Release\net10.0-android\`.

## Desktop Setup

1. Open **Settings → Mobile**.
2. Enable **Mobile sync**.
3. Choose a bind mode:
   - Public tunnel or reverse proxy: bind `127.0.0.1` and disable **Allow LAN access**. The proxy connects locally.
   - Direct LAN access: enable **Allow LAN access**. The bind address becomes `0.0.0.0`.
4. Set the port (default `47921`).
5. Set **Public base URL** to the HTTPS URL the phone will use, for example `https://aia.example.com`.
6. Save.
7. Click **Generate code**. The desktop shows an 8-digit code and a QR code.

### Public HTTPS

Recommended: terminate TLS outside AIA.

Cloudflare Tunnel quick test:

```powershell
cloudflared tunnel --url http://127.0.0.1:47921
```

Use the generated `https://...trycloudflare.com` host as **Public base URL**. For permanent use, create a named tunnel.

Tailscale Funnel is also supported when the funnel points at `http://127.0.0.1:47921`.

Do not port-forward Kestrel directly. If you terminate TLS inside AIA, enable **Use HTTPS** and provide a PFX path and password. The password is DPAPI-protected at rest.

## Pairing

The QR payload is:

```
aia://pair?url=https%3A%2F%2Fhost&code=12345678
```

Scan it with a normal phone camera/QR app. Android opens AIA and pairs automatically through the
`aia://pair` intent filter. If the camera does not open the app, enter the server URL and code
manually on the Android **Settings** page.

Pairing codes are single use and expire after 5 minutes. Device tokens are 256-bit random values.
The desktop stores only SHA-256 hashes in a DPAPI-encrypted file. Revoke a device at any time in
**Settings → Mobile → Connected devices**.

## Android Behavior

- Sync runs on app start, pull-to-refresh, manual **Sync**, and a periodic Android JobScheduler job
  (15-minute minimum).
- Local edits work offline. Pending changes are pushed on the next successful sync.
- On a revision conflict, the server version is applied. The phone's local copy is replaced.
- Notifications are scheduled for due reminders and calendar events.
- **Mirror tasks to phone calendar** creates a local calendar named `AIA`.
  - AIA events and due reminders are written to it.
  - Edits made in the `AIA` calendar are imported back into the app.
  - External calendars are not read or modified.
- Permissions: `READ_CALENDAR`, `WRITE_CALENDAR`, `POST_NOTIFICATIONS`, `INTERNET`,
  `ACCESS_NETWORK_STATE`. Denying calendar or notification permission only disables those features.

## Data Files

Desktop:

```
%LOCALAPPDATA%\AIA\userdata\tasks.json
%LOCALAPPDATA%\AIA\userdata\reminders.json
%LOCALAPPDATA%\AIA\userdata\calendar-events.json
%LOCALAPPDATA%\AIA\userdata\sync-journal.json
%LOCALAPPDATA%\AIA\settings\mobile-sync-devices.json   (DPAPI encrypted)
```

Android:

```
<app data>/aia-mobile.db
Android SecureStorage: device token
```

The sync journal is a sidecar. Existing task/reminder JSON files remain compatible.

## API

All `/api/v1/sync` routes require `Authorization: Bearer <device-token>`.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/health` | Status and server revision |
| POST | `/api/v1/pair` | Exchange one-time code for device token |
| GET | `/api/v1/sync?since=N` | Pull changes after revision N |
| POST | `/api/v1/sync` | Push changes and pull everything after `sinceRevision` |

Limits: 1 MB request body, 120 API requests/minute, 20 pairing requests/10 minutes, 5 wrong codes
per address per 10 minutes.

## Security Checklist

- Public traffic must be HTTPS. Plain HTTP is only for local development.
- Keep the default loopback bind when using a tunnel.
- Never share the pairing code or device token.
- Revoke lost devices in **Settings → Mobile**.
- Restrict the tunnel hostname to trusted users; the token is the only authorization layer.
- Do not put task content into logs. The server logs errors only.

## Troubleshooting

**Phone cannot reach the desktop**

- Confirm sync is **Running** in **Settings → Mobile**.
- Test from the phone browser: `https://your-host/api/v1/health`.
- For LAN: allow the port in Windows Firewall and enable **Allow LAN access**.
- The server enforces an allowed-host list. Make sure **Public base URL** matches the hostname the
  phone uses.

**401 Unauthorized**

- Pair again. The device token may have been revoked or the device file may belong to another
  Windows user.

**Android build says workload missing**

```powershell
dotnet workload install maui-android
```

Then install JDK 17 and set `JAVA_HOME`.

**Calendar events do not appear**

- Grant calendar permissions in Android app settings.
- Check that the `AIA` calendar is visible in the phone calendar app.
- External calendars are intentionally not modified.

## Limitations

- The desktop app must be running.
- Conflicts are resolved server-wins; phone edits are not kept as conflict copies.
- No iOS client, web client, or cloud-provider OAuth sync.
- Chat, data banks, clipboard, and screenshots are not synced.
