# Updates

FluxDisplay checks GitHub Releases for a newer `FluxDisplay-<version>-setup.exe`,
downloads it, verifies it and installs silently. No code signing is used
(deliberate product decision, see limits below).

## Modes (Settings → Updates, default: Automatic)

| Mode | Installed copy | Portable copy |
|---|---|---|
| `Automatic` | Check on startup; download, verify and silent-install with no prompts. | Check on startup; ask before downloading (a portable copy cannot be replaced in place — the installer creates/updates the installed copy). |
| `NotifyOnly` | Check on startup; ask before downloading. | Same as installed. |
| `Disabled` | Never check, no requests. | Never check, no requests. |

Checks also run every 6 hours (single-flight with manual/startup checks) and can be
triggered with **Check now**. HTTP 404 on `/latest` (no stable release) is a normal
"No updates available", not an error.

## Integrity policy

- Only `sha256:<hex>` digests from the release metadata are accepted.
- The downloaded file's SHA-256 is computed while streaming and must match
  before setup starts; on mismatch the file is deleted and nothing runs.
- A missing digest blocks the **silent** path ("Release has no checksum.
  Automatic install is disabled."). A user-confirmed install may proceed after
  an explicit warning that the file cannot be verified.
- The digest proves the file matches GitHub metadata. It is not a publisher
  signature and does not replace code signing.

## Install handoff

1. The verified setup starts with
   `/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /CLOSEAPPLICATIONS`.
2. The app exits (`Application.Current.Exit`, watchdog `Environment.Exit` after
   5 s) so its files unlock. Settings and presets are saved eagerly on every
   change, so killing the process loses nothing.
3. Inno (`CloseApplications=yes`, `RestartApplications=no`) replaces the files.
4. Exactly one restart mechanism exists: the `[Run]` entry with
   `skipifnotsilent` relaunches the app after silent updates (the interactive
   checkbox entry is skipped in silent mode, and vice versa). No
   `RegisterApplicationRestart`, no Restart Manager restarts.
5. Install log: `%TEMP%\FluxDisplay-setup.log`. App log: `%LocalAppData%\FluxDisplay\logs\`.

## Limits (unsigned builds)

- SmartScreen, Smart App Control, Defender, WDAC/AppLocker and corporate
  policies may warn about or block the unsigned setup. This cannot be worked
  around in code; the user must allow it explicitly.
- Per-user install needs no admin/UAC. If elevation were ever required, the
  app never exits before the installer process successfully starts, and the
  relaunched app would inherit the installer's token — verify rights in that
  scenario before shipping such a configuration.
- No automatic rollback. To go back: save `%LocalAppData%\FluxDisplay`
  (settings + presets), run the previous version's setup manually, restore the
  folder if needed. Downgrade compatibility of settings is not guaranteed —
  verify before relying on it.

## Recovery

If an update fails midway (network cut, disk full, hash mismatch): the partial
file is deleted, the old installed copy keeps working, the status shows what
failed. Re-run **Check now** or download the setup from GitHub Releases and run
it manually. If the app no longer starts after an update, reinstall the previous
release's setup over it (see rollback note above).
