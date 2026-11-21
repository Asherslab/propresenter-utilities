# Installing and running as a service

This app is an ordinary ASP.NET Core process. It integrates with the Windows service manager and
systemd automatically, and runs happily under launchd on macOS. One build works for all three.

- [Quick install (macOS and Linux)](#quick-install-macos-and-linux)
- [Publishing a release](#publishing-a-release)
- [Prerequisites](#prerequisites)
- [Building](#building)
- [Configuration](#configuration)
- [macOS — launchd](#macos--launchd)
- [Windows — service](#windows--service)
- [Linux — systemd](#linux--systemd)
- [Network exposure](#network-exposure)
- [Upgrading and uninstalling](#upgrading-and-uninstalling)
- [Troubleshooting](#troubleshooting)

## Quick install (macOS and Linux)

On the machine that runs ProPresenter, as the booth user (not with `sudo`):

```bash
curl -fsSL https://raw.githubusercontent.com/Asherslab/propresenter-utilities/main/install.sh | bash
```

That downloads the latest [release](https://github.com/Asherslab/propresenter-utilities/releases) for
this machine, checks its SHA-256, and runs it as a per-user service: a LaunchAgent on macOS, a systemd
user unit on Linux. No .NET install is needed; release builds are self-contained. Then open
<http://127.0.0.1:8752>.

**To update, run the same command again.** It does nothing if the latest version is already installed.
Otherwise it stops the service, switches to the new version and checks that the control panel
answers. If it doesn't, it puts the previous version back.

| | macOS | Linux |
| --- | --- | --- |
| App | `~/Applications/propresenter-utilities` | `~/.local/share/propresenter-utilities` |
| Service | `~/Library/LaunchAgents/com.propresenter.utilities.plist` | `~/.config/systemd/user/propresenter-utilities.service` |
| Logs | `~/Library/Logs/propresenter-utilities.log` | `journalctl --user -u propresenter-utilities` |
| Config | `~/.config/propresenter-utilities/config.json` | same |

Options go after `bash -s --`, for example `curl -fsSL … | bash -s -- --urls http://0.0.0.0:8752`:

| Option | Meaning |
| --- | --- |
| `--version v1.2.0` | Install a specific release, e.g. to roll back |
| `--force` | Reinstall even if that version is already installed |
| `--urls URL` | Address the control panel listens on (see [Network exposure](#network-exposure)) |
| `--config PATH` | Configuration file location |
| `--dir PATH` | Install directory |
| `--uninstall` | Remove the service and the app. The configuration file is kept |

An update keeps the `--urls` and `--config` values the existing LaunchAgent was installed with, so
they only need passing once.

**Replacing an existing install (macOS).** The installer looks in `~/Library/LaunchAgents` for any
other agent that runs `ProPresenter.App` or `ProPresenter.App.dll`, for example one created by hand
or by an IDE deployment. It then:

1. Takes that agent's `--urls`/`--config` settings, from its environment or arguments. If it has none,
   the defaults match what the app uses anyway, so the same rules file is kept.
2. Stops it, and moves its plist to `~/Library/Application Support/propresenter-utilities/replaced-agents/`.
   Its program files are left in place.
3. Installs and starts the new service. If that fails on a first install, it removes the new service
   and puts the old agent back.

The check runs on every run, including "already installed" ones, so an IDE that recreates its agent
is cleared again the next time the installer runs. Disable that deployment so it stops recreating it.

If something that isn't a LaunchAgent is still serving the control panel's port (the app run from a
terminal or an IDE), the installer stops and says so rather than starting a second copy.

On Linux, a user service only runs while you're logged in unless lingering is on:
`sudo loginctl enable-linger $USER`. On Windows, download the `win-x64` zip from the releases page
and use [the service script](#windows--service).

## Publishing a release

Releases are built by GitHub Actions (`.github/workflows/release.yml`) when a version tag is pushed:

```bash
git tag v1.2.0
git push origin v1.2.0
```

The workflow runs the tests, then publishes self-contained builds for `osx-arm64`, `osx-x64`,
`linux-x64`, `linux-arm64` and `win-x64`, and creates the release with a `SHA256SUMS` file and notes
generated from the commits. A tag with a suffix, such as `v1.2.0-rc.1`, becomes a pre-release, which
the installer's "latest" skips. Install it with `--version`.

Every push and pull request also runs the build and tests (`.github/workflows/ci.yml`).

## Prerequisites

- **ProPresenter 7.9 or newer** with the network link enabled: **Preferences → Network → Enable
  Network**. Note the port (54937 by default). Developed and verified against ProPresenter 21.4.
- **.NET 10 SDK** to build. The runtime alone is enough to run a framework-dependent build.

The ProPresenter API has no authentication, so there is no password to configure.

## Building

From the repository root:

```bash
dotnet publish src/ProPresenter.App -c Release -o /path/to/install
```

That produces a framework-dependent build, which needs the .NET 10 runtime on the target machine and
is started with `dotnet ProPresenter.App.dll`.

To avoid installing a runtime on the production machine, publish self-contained for its platform:

```bash
dotnet publish src/ProPresenter.App -c Release -r osx-arm64 --self-contained -o /path/to/install
```

Use `osx-arm64` (Apple Silicon), `osx-x64` (Intel Mac), `win-x64`, or `linux-x64`. The result is
started by running the `ProPresenter.App` executable directly.

> Adding `-p:PublishSingleFile=true` collapses the output into one file. On macOS an unsigned
> single-file bundle is killed by the OS on launch; sign it with `codesign -s - --force
> ./ProPresenter.App` if you use that option. The plain self-contained publish above avoids the issue
> and is the recommended path.

Pick an install location the service account can read, for example:

- macOS — `/usr/local/propresenter-utilities` or `~/Applications/propresenter-utilities`
- Windows — `C:\Program Files\ProPresenter Utilities`
- Linux — `/opt/propresenter-utilities`

## Configuration

Two settings are worth knowing before writing a service definition. Both can be given as environment
variables, and both are optional.

| Environment variable | Default | Meaning |
| --- | --- | --- |
| `PROPRESENTER_UTILITIES_URLS` | `http://127.0.0.1:8752` | Address the web UI listens on |
| `PROPRESENTER_UTILITIES_CONFIG` | See below | Path to the profiles/rules JSON file |

They can also be passed on the command line as `--urls` and `--config`.

The ProPresenter host and port are **not** environment variables — they live in the config file and
are set from the Settings page, so they can be changed without touching the service definition.

Default config file location:

| Platform | Path |
| --- | --- |
| macOS / Linux | `~/.config/propresenter-utilities/config.json` |
| Windows | `%APPDATA%\propresenter-utilities\config.json` |

A service running as a different user than the one you configured the app with will look in *that
user's* directory and appear to have lost your rules. Set `PROPRESENTER_UTILITIES_CONFIG` explicitly
in the service definition to avoid this entirely.

## macOS — launchd

macOS uses launchd, not systemd. Use a **LaunchAgent** so the service runs as your booth user and
under the same login session as ProPresenter.

Copy [`deploy/macos/com.propresenter.utilities.plist`](../deploy/macos/com.propresenter.utilities.plist)
to `~/Library/LaunchAgents/`, editing the paths inside it first:

```bash
mkdir -p ~/Library/LaunchAgents
cp deploy/macos/com.propresenter.utilities.plist ~/Library/LaunchAgents/
$EDITOR ~/Library/LaunchAgents/com.propresenter.utilities.plist
```

Load and start it:

```bash
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.propresenter.utilities.plist
launchctl kickstart -p gui/$(id -u)/com.propresenter.utilities
```

Check status and logs:

```bash
launchctl print gui/$(id -u)/com.propresenter.utilities | head -20
tail -f ~/Library/Logs/propresenter-utilities.log
```

Stop, or remove entirely:

```bash
launchctl kill SIGTERM gui/$(id -u)/com.propresenter.utilities   # stop
launchctl bootout gui/$(id -u)/com.propresenter.utilities        # unload
```

`RunAtLoad` plus `KeepAlive` means it starts at login and is restarted if it exits.

> Use a LaunchDaemon in `/Library/LaunchDaemons` only if you need it running before anyone logs in.
> It then runs as `root` with a different home directory, so set `PROPRESENTER_UTILITIES_CONFIG` to an
> absolute path and be aware ProPresenter itself will not be running yet.

## Windows — service

The host already calls `UseWindowsService()`, so the published executable can be registered directly.
Run these in an **elevated** PowerShell:

```powershell
New-Service -Name "ProPresenterUtilities" `
  -DisplayName "ProPresenter Utilities" `
  -Description "Fires ProPresenter cues from timer triggers." `
  -BinaryPathName '"C:\Program Files\ProPresenter Utilities\ProPresenter.App.exe" --config "C:\ProgramData\propresenter-utilities\config.json"' `
  -StartupType Automatic

Start-Service ProPresenterUtilities
```

A ready-made script is in
[`deploy/windows/install-service.ps1`](../deploy/windows/install-service.ps1).

Because the service runs as `LocalSystem` by default, always pass `--config` with an absolute path as
shown, or point the service at a dedicated account.

Manage it with:

```powershell
Get-Service ProPresenterUtilities
Restart-Service ProPresenterUtilities
Stop-Service ProPresenterUtilities
sc.exe delete ProPresenterUtilities   # uninstall, after stopping
```

Service output goes to the Windows Event Log:

```powershell
Get-EventLog -LogName Application -Source "ProPresenter Utilities" -Newest 20
```

For a framework-dependent build, point `-BinaryPathName` at
`"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\ProPresenter Utilities\ProPresenter.App.dll"`.

## Linux — systemd

The host calls `UseSystemd()`, so it reports readiness correctly with `Type=notify`.

Copy [`deploy/linux/propresenter-utilities.service`](../deploy/linux/propresenter-utilities.service)
to `/etc/systemd/system/`, edit the user and paths, then:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now propresenter-utilities
```

```bash
systemctl status propresenter-utilities
journalctl -u propresenter-utilities -f
```

To run it for a single desktop user instead, put the unit in `~/.config/systemd/user/` and use
`systemctl --user enable --now propresenter-utilities`.

## Network exposure

The default `http://127.0.0.1:8752` means the control panel is reachable only from the machine it
runs on. That is the right default: **neither this UI nor the ProPresenter API requires a password**,
so anything that can reach the port can fire cues and change your service configuration. The same
applies to the `/api/signals/…` webhook, which is unauthenticated by design.

The RossTalk listener is separate and **does** bind to all interfaces on its own port (7788 by
default), because ProPresenter has to be able to dial into it — possibly from another machine. It only
ever accepts short text commands and can do nothing but raise signals you have configured rules for,
but it is still a port open on your network; see [SIGNALS.md](SIGNALS.md). Switch it off on the
Signals page if you are not using macro triggers.

To open it to a tablet at the sound desk, set:

```
PROPRESENTER_UTILITIES_URLS=http://0.0.0.0:8752
```

Only do this on a trusted, isolated production network, and consider restricting it with a firewall
rule to the specific devices that need it. There is no authentication to fall back on.

## Upgrading and uninstalling

With the quick installer, re-run it to upgrade, or pass `--uninstall`; see
[Quick install](#quick-install-macos-and-linux). For a manual install, upgrading is a file replacement: stop the service, publish over the install directory, start it again.
The config file lives outside the install directory and is untouched.

Uninstalling leaves your profiles behind; delete the config file too if you want them gone. Nothing is
written into ProPresenter itself except the timers you created from the Timers page.

## Troubleshooting

**The dashboard says "Not connected".** Check the host and port on the Settings page and press
**Test** — it reports the ProPresenter version on success. Confirm the network link is enabled in
ProPresenter's preferences, and that a firewall is not blocking the port.

**Timers appear but no cues fire.** Check the **Triggers armed** switch on the dashboard, that the
right profile is active, and that each rule is enabled and has at least one action. The Triggers page
flags rules with no actions and rules whose timer no longer exists. Use **Test fire** to confirm the
action itself works.

**Cues fire late or not at all after restarting mid-service.** That is deliberate: points already
passed when the app connects are treated as done, so a restart does not replay them. Resetting the
timer re-arms everything.

**My rules disappeared after installing as a service.** The service is almost certainly running as a
different user and reading a different config file. Set `PROPRESENTER_UTILITIES_CONFIG` to an absolute
path in the service definition, and copy your existing config file there.

**The UI loads unstyled.** The published output must include the `wwwroot` folder and the
`*.staticwebassets.endpoints.json` file next to the executable. Republish rather than copying the
executable alone.

**Port 8752 is already in use.** Change `PROPRESENTER_UTILITIES_URLS` to another port.

**The Signals page says the RossTalk listener failed.** Something else holds port 7788. Change the
port on the Signals page and update the RossTalk device in ProPresenter's preferences to match.
