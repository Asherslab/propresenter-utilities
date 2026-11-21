#!/usr/bin/env bash
#
# Installs or updates ProPresenter Utilities from the latest GitHub release, and runs it as a
# per-user service: a LaunchAgent on macOS, a systemd user unit on Linux.
#
#   curl -fsSL https://raw.githubusercontent.com/Asherslab/propresenter-utilities/main/install.sh | bash
#
# Run it again to update. Options (with curl | bash, pass them as: bash -s -- --force):
#
#   --version vX.Y.Z   install a specific release instead of the latest
#   --force            reinstall even if that version is already installed
#   --urls URL         address the control panel listens on (default http://127.0.0.1:8752)
#   --config PATH      configuration file (default ~/.config/propresenter-utilities/config.json)
#   --dir PATH         install directory
#   --uninstall        stop and remove the service and the app; the configuration file is kept
#
# Each version is unpacked side by side under <dir>/versions and <dir>/current points at the live one.
# If a new version does not come up, the previous one is put back.

set -euo pipefail

REPO="Asherslab/propresenter-utilities"
# Overridable for a mirror, or for testing against a local server.
RELEASES="${PROPRESENTER_UTILITIES_RELEASES:-https://github.com/$REPO/releases}"
LABEL="com.propresenter.utilities"
UNIT="propresenter-utilities"
EXE="ProPresenter.App"

say() { printf '\033[1m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[33mwarning:\033[0m %s\n' "$*" >&2; }
die() { printf '\033[31merror:\033[0m %s\n' "$*" >&2; exit 1; }

# ------------------------------------------------------------------ platform

detect_platform() {
    case "$(uname -s)" in
        Darwin) OS=osx ;;
        Linux) OS=linux ;;
        *) die "Unsupported OS $(uname -s). On Windows, download the win-x64 zip from the releases page." ;;
    esac

    case "$(uname -m)" in
        arm64 | aarch64) ARCH=arm64 ;;
        x86_64 | amd64) ARCH=x64 ;;
        *) die "Unsupported CPU $(uname -m)." ;;
    esac

    RID="$OS-$ARCH"
    ASSET="propresenter-utilities-$RID.tar.gz"

    if [ "$OS" = osx ]; then
        DEFAULT_DIR="$HOME/Applications/propresenter-utilities"
        PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
        LOG="$HOME/Library/Logs/propresenter-utilities.log"
    else
        DEFAULT_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/propresenter-utilities"
        UNIT_FILE="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user/$UNIT.service"
    fi
}

sha256() {
    if command -v sha256sum >/dev/null; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

# ------------------------------------------------------------------- service

# Settings from an existing LaunchAgent win over defaults, so an update never silently rebinds the
# control panel or points the service at a different configuration file.
read_existing_settings() {
    [ "$OS" = osx ] && [ -f "$PLIST" ] || return 0
    local pb=/usr/libexec/PlistBuddy
    [ -n "$URLS" ] || URLS="$($pb -c 'Print :EnvironmentVariables:PROPRESENTER_UTILITIES_URLS' "$PLIST" 2>/dev/null || true)"
    [ -n "$CONFIG" ] || CONFIG="$($pb -c 'Print :EnvironmentVariables:PROPRESENTER_UTILITIES_CONFIG' "$PLIST" 2>/dev/null || true)"
}

stop_service() {
    if [ "$OS" = osx ]; then
        launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
        # bootout returns before the process has always gone.
        for _ in $(seq 1 20); do
            launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1 || break
            sleep 0.5
        done
    else
        systemctl --user stop "$UNIT" 2>/dev/null || true
    fi
}

write_service() {
    local exe="$INSTALL_DIR/current/$EXE"
    if [ "$OS" = osx ]; then
        mkdir -p "$(dirname "$PLIST")" "$(dirname "$LOG")"
        cat >"$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!-- Written by install.sh; re-running it rewrites this file, keeping the URLS and CONFIG values. -->
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$exe</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>PROPRESENTER_UTILITIES_URLS</key>
        <string>$URLS</string>
        <key>PROPRESENTER_UTILITIES_CONFIG</key>
        <string>$CONFIG</string>
        <key>DOTNET_NOLOGO</key>
        <string>1</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>ThrottleInterval</key>
    <integer>10</integer>
    <key>StandardOutPath</key>
    <string>$LOG</string>
    <key>StandardErrorPath</key>
    <string>$LOG</string>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>
EOF
    else
        mkdir -p "$(dirname "$UNIT_FILE")"
        cat >"$UNIT_FILE" <<EOF
# Written by install.sh; re-running it rewrites this file.
[Unit]
Description=ProPresenter Utilities
Documentation=https://github.com/$REPO
After=network-online.target

[Service]
Type=notify
ExecStart=$exe
Environment=PROPRESENTER_UTILITIES_URLS=$URLS
Environment=PROPRESENTER_UTILITIES_CONFIG=$CONFIG
Environment=DOTNET_NOLOGO=1
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
EOF
        systemctl --user daemon-reload
    fi
}

start_service() {
    if [ "$OS" = osx ]; then
        # Re-bootstrapping straight after a bootout can fail transiently with an I/O error.
        for _ in 1 2 3 4 5; do
            launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null && return 0
            sleep 1
        done
        die "launchctl could not load $PLIST"
    else
        systemctl --user enable "$UNIT" >/dev/null 2>&1
        systemctl --user restart "$UNIT"
    fi
}

# The first address the app listens on, rewritten so it can be reached from this machine.
health_url() {
    local url="${URLS%%;*}"
    url="${url/0.0.0.0/127.0.0.1}"
    url="${url/\/\/\*/\/\/127.0.0.1}"
    url="${url/\/\/+/\/\/127.0.0.1}"
    url="${url/\[::\]/127.0.0.1}"
    echo "${url%/}/"
}

responds() { curl -fsS -o /dev/null --max-time 2 "$(health_url)" 2>/dev/null; }

wait_healthy() {
    for _ in $(seq 1 30); do
        responds && return 0
        sleep 1
    done
    return 1
}

# ------------------------------------------------------------------- actions

uninstall() {
    say "Stopping and removing the service"
    stop_service
    if [ "$OS" = osx ]; then
        rm -f "$PLIST"
    else
        systemctl --user disable "$UNIT" >/dev/null 2>&1 || true
        rm -f "$UNIT_FILE"
        systemctl --user daemon-reload
    fi
    rm -rf "$INSTALL_DIR"
    say "Removed. Your configuration was left at ${CONFIG}."
}

resolve_tag() {
    if [ -n "$TAG" ]; then return; fi
    # releases/latest redirects to releases/tag/<tag>; no API token or JSON parsing needed.
    local effective
    effective="$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$RELEASES/latest")" \
        || die "Could not reach GitHub."
    TAG="${effective##*/}"
    [ -n "$TAG" ] && [ "$TAG" != latest ] && [ "$TAG" != releases ] || die "$REPO has no releases yet."
}

install() {
    resolve_tag

    # Recorded only once a version has come up healthy, so a failed install is retried on the next run.
    local installed=""
    [ -f "$INSTALL_DIR/installed-version" ] && installed="$(cat "$INSTALL_DIR/installed-version")"
    if [ "$installed" = "$TAG" ] && [ "$FORCE" = 0 ]; then
        say "$TAG is already installed. Use --force to reinstall."
        return
    fi

    say "Installing $TAG ($RID)${installed:+, replacing $installed}"

    TMP="$(mktemp -d)"
    trap 'rm -rf "$TMP"' EXIT
    local tmp="$TMP"

    local base="$RELEASES/download/$TAG"
    curl -fL --progress-bar -o "$tmp/$ASSET" "$base/$ASSET" || die "No $ASSET in release $TAG."
    curl -fsSL -o "$tmp/SHA256SUMS" "$base/SHA256SUMS" || die "No SHA256SUMS in release $TAG."

    local expected actual
    expected="$(awk -v f="$ASSET" '$2 == f || $2 == "*"f {print $1}' "$tmp/SHA256SUMS")"
    actual="$(sha256 "$tmp/$ASSET")"
    [ -n "$expected" ] && [ "$expected" = "$actual" ] || die "Checksum mismatch for $ASSET."

    local target="$INSTALL_DIR/versions/$TAG"
    rm -rf "$target"
    mkdir -p "$target"
    tar -xzf "$tmp/$ASSET" -C "$target" --strip-components=1
    chmod +x "$target/$EXE"

    if [ "$OS" = osx ]; then
        xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
        # Release builds are signed on CI; this only matters for a build that somehow is not.
        codesign -v "$target/$EXE" 2>/dev/null || codesign -s - --force "$target/$EXE"
    fi

    local previous=""
    [ -L "$INSTALL_DIR/current" ] && previous="$(readlink "$INSTALL_DIR/current")"

    stop_service

    if responds; then
        # Our service is stopped, so this is something else: likely an install made by hand.
        [ -n "$previous" ] && start_service
        die "Something else is already serving $(health_url). Stop it (an older manual install?) and re-run."
    fi

    ln -sfn "versions/$TAG" "$INSTALL_DIR/current"
    write_service
    start_service

    if wait_healthy; then
        echo "$TAG" >"$INSTALL_DIR/installed-version"
        say "$TAG is running at ${URLS%%;*}"
    elif [ -n "$previous" ] && [ "$previous" != "versions/$TAG" ]; then
        warn "$TAG did not respond on $(health_url); rolling back to ${previous#versions/}."
        stop_service
        ln -sfn "$previous" "$INSTALL_DIR/current"
        rm -rf "$target"
        start_service
        die "Update failed. ${LOG:+See $LOG}${UNIT_FILE:+See journalctl --user -u $UNIT}"
    else
        die "$TAG did not respond on $(health_url). ${LOG:+See $LOG}${UNIT_FILE:+See journalctl --user -u $UNIT}"
    fi

    # Keep the live version and the one before it, for a manual rollback.
    local keep_prev="${previous#versions/}"
    for dir in "$INSTALL_DIR"/versions/*; do
        local name="${dir##*/}"
        [ "$name" = "$TAG" ] || [ "$name" = "$keep_prev" ] || rm -rf "$dir"
    done

    if [ "$OS" = linux ] && ! loginctl show-user "$USER" -p Linger 2>/dev/null | grep -q yes; then
        warn "To start at boot without logging in, run: sudo loginctl enable-linger $USER"
    fi
}

main() {
    TAG="" FORCE=0 UNINSTALL=0 URLS="" CONFIG="" INSTALL_DIR=""
    while [ $# -gt 0 ]; do
        case "$1" in
            --version) TAG="${2:?--version needs a tag}"; shift 2 ;;
            --force) FORCE=1; shift ;;
            --urls) URLS="${2:?--urls needs a value}"; shift 2 ;;
            --config) CONFIG="${2:?--config needs a path}"; shift 2 ;;
            --dir) INSTALL_DIR="${2:?--dir needs a path}"; shift 2 ;;
            --uninstall) UNINSTALL=1; shift ;;
            -h | --help) sed -n '2,20p' "$0" 2>/dev/null | sed 's/^# \{0,1\}//'; exit 0 ;;
            *) die "Unknown option $1" ;;
        esac
    done

    detect_platform
    read_existing_settings
    INSTALL_DIR="${INSTALL_DIR:-$DEFAULT_DIR}"
    URLS="${URLS:-http://127.0.0.1:8752}"
    CONFIG="${CONFIG:-$HOME/.config/propresenter-utilities/config.json}"

    if [ "$UNINSTALL" = 1 ]; then uninstall; else install; fi
}

# Everything is inside functions, so a truncated download through curl | bash runs nothing.
main "$@"
