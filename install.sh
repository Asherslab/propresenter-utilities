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

PB=/usr/libexec/PlistBuddy

# Settings from an existing LaunchAgent win over defaults, so an update never silently rebinds the
# control panel or points the service at a different configuration file. Reads the environment and
# any --urls/--config arguments; values already set (from flags, or an earlier plist) are kept.
read_settings_from() {
    local plist="$1" i=0 arg next
    [ -n "$URLS" ] || URLS="$($PB -c 'Print :EnvironmentVariables:PROPRESENTER_UTILITIES_URLS' "$plist" 2>/dev/null || true)"
    [ -n "$CONFIG" ] || CONFIG="$($PB -c 'Print :EnvironmentVariables:PROPRESENTER_UTILITIES_CONFIG' "$plist" 2>/dev/null || true)"
    while arg="$($PB -c "Print :ProgramArguments:$i" "$plist" 2>/dev/null)"; do
        next="$($PB -c "Print :ProgramArguments:$((i + 1))" "$plist" 2>/dev/null || true)"
        case "$arg" in
            --urls) [ -n "$URLS" ] || URLS="$next" ;;
            --urls=*) [ -n "$URLS" ] || URLS="${arg#--urls=}" ;;
            --config) [ -n "$CONFIG" ] || CONFIG="$next" ;;
            --config=*) [ -n "$CONFIG" ] || CONFIG="${arg#--config=}" ;;
        esac
        i=$((i + 1))
    done
}

read_existing_settings() {
    [ "$OS" = osx ] || return 0
    if [ -f "$PLIST" ]; then read_settings_from "$PLIST"; fi

    local f
    while IFS= read -r f; do
        if [ -n "$f" ]; then read_settings_from "$f"; fi
    done <<<"$LEGACY"
}

# LaunchAgents that run this app under a label other than ours: an install made by hand, or one
# deployed from an IDE. Left running, they would hold the same ports as the service installed here.
# Newline-separated, because macOS's bash 3.2 cannot expand an empty array under `set -u`.
find_legacy_agents() {
    LEGACY=""
    [ "$OS" = osx ] || return 0
    local f
    for f in "$HOME"/Library/LaunchAgents/*.plist; do
        [ -f "$f" ] && [ "$f" != "$PLIST" ] || continue
        [ "$($PB -c 'Print :Label' "$f" 2>/dev/null || true)" != "$LABEL" ] || continue
        if $PB -c 'Print :ProgramArguments' "$f" 2>/dev/null | grep -qE '/ProPresenter\.App(\.dll)?$'; then
            LEGACY="$LEGACY$f"$'\n'
        fi
    done
}

# Where replaced agents' plists are parked: outside the install directory, so --uninstall keeps them,
# and outside LaunchAgents, so launchd does not load them at the next login.
PARKED="$HOME/Library/Application Support/propresenter-utilities/replaced-agents"

wait_unloaded() {
    for _ in $(seq 1 20); do
        launchctl print "gui/$(id -u)/$1" >/dev/null 2>&1 || return 0
        sleep 0.5
    done
}

# Stops each legacy agent and parks its plist. RETIRED lists them, for restore_legacy.
retire_legacy() {
    RETIRED=""
    local f label
    while IFS= read -r f; do
        [ -n "$f" ] || continue
        label="$($PB -c 'Print :Label' "$f" 2>/dev/null || true)"
        say "Replacing the existing install '$label' ($(basename "$f"))"
        launchctl bootout "gui/$(id -u)/$label" 2>/dev/null || true
        wait_unloaded "$label"
        mkdir -p "$PARKED"
        mv -f "$f" "$PARKED/"
        RETIRED="$RETIRED$(basename "$f")"$'\n'
    done <<<"$LEGACY"

    if [ -n "$RETIRED" ]; then
        say "Its plist was moved to $PARKED. Its program files were left where they were."
        warn "If an IDE deployment (e.g. Rider over SSH) created it, disable that deployment so it is not recreated."
    fi
}

# Puts the retired agents back and starts them, when the new install cannot take over.
restore_legacy() {
    local name
    while IFS= read -r name; do
        [ -n "$name" ] || continue
        warn "Restoring the previous install ($name)"
        mv -f "$PARKED/$name" "$HOME/Library/LaunchAgents/$name"
        launchctl bootstrap "gui/$(id -u)" "$HOME/Library/LaunchAgents/$name" 2>/dev/null || true
    done <<<"$RETIRED"
    RETIRED=""
}

stop_service() {
    if [ "$OS" = osx ]; then
        launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
        # bootout returns before the process has always gone.
        wait_unloaded "$LABEL"
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
        rm -f -- "${PLIST:?}"
    else
        systemctl --user disable "$UNIT" >/dev/null 2>&1 || true
        rm -f -- "${UNIT_FILE:?}"
        systemctl --user daemon-reload
    fi
    # Only what install() creates, never the directory wholesale: --dir pointed at the wrong place
    # must not be able to take anything else with it.
    if [ -d "${INSTALL_DIR:?}/versions" ] || [ -L "$INSTALL_DIR/current" ]; then
        rm -rf -- "${INSTALL_DIR:?}/versions"
        rm -f -- "$INSTALL_DIR/current" "$INSTALL_DIR/installed-version"
        rmdir -- "$INSTALL_DIR" 2>/dev/null || warn "Left $INSTALL_DIR in place: it contains other files."
        say "Removed. Your configuration was left at ${CONFIG}."
    else
        warn "$INSTALL_DIR does not look like an install made by this script; left it alone."
        say "Service removed. Your configuration was left at ${CONFIG}."
    fi
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

# A tag becomes a directory name under versions/, so it must not be able to name a path elsewhere.
validate_tag() {
    case "$TAG" in
        "" | . | .. | *[!A-Za-z0-9._-]*) die "'$TAG' is not a release tag." ;;
    esac
}

install() {
    resolve_tag
    validate_tag

    # Recorded only once a version has come up healthy, so a failed install is retried on the next run.
    local installed=""
    [ -f "$INSTALL_DIR/installed-version" ] && installed="$(cat "$INSTALL_DIR/installed-version")"
    if [ "$installed" = "$TAG" ] && [ "$FORCE" = 0 ]; then
        say "$TAG is already installed. Use --force to reinstall."
        # Still clear out another install that has appeared since, e.g. an IDE redeploying.
        if [ -n "$LEGACY" ]; then
            retire_legacy
            launchctl kickstart -k "gui/$(id -u)/$LABEL" 2>/dev/null || true
        fi
        return
    fi

    say "Installing $TAG ($RID)${installed:+, replacing $installed}"

    TMP="$(mktemp -d)"
    trap 'rm -rf -- "${TMP:?}"' EXIT
    local tmp="$TMP"

    local base="$RELEASES/download/$TAG"
    curl -fL --progress-bar -o "$tmp/$ASSET" "$base/$ASSET" || die "No $ASSET in release $TAG."
    curl -fsSL -o "$tmp/SHA256SUMS" "$base/SHA256SUMS" || die "No SHA256SUMS in release $TAG."

    local expected actual
    expected="$(awk -v f="$ASSET" '$2 == f || $2 == "*"f {print $1}' "$tmp/SHA256SUMS")"
    actual="$(sha256 "$tmp/$ASSET")"
    [ -n "$expected" ] && [ "$expected" = "$actual" ] || die "Checksum mismatch for $ASSET."

    local target="${INSTALL_DIR:?}/versions/${TAG:?}"
    rm -rf -- "$target"
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
    retire_legacy

    if responds; then
        # Every agent we know of is stopped, so this is something else: the app run from a terminal or
        # an IDE, or a service under another service manager.
        restore_legacy
        [ -n "$previous" ] && start_service
        die "Something else is already serving $(health_url). Stop it and re-run."
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
        rm -rf -- "${target:?}"
        start_service
        die "Update failed. ${LOG:+See $LOG}${UNIT_FILE:+See journalctl --user -u $UNIT}"
    else
        # First install, nothing of ours to fall back to: remove it, so it cannot compete with the
        # install it was replacing, and put that back.
        stop_service
        if [ "$OS" = osx ]; then rm -f -- "${PLIST:?}"; fi
        restore_legacy
        die "$TAG did not respond on $(health_url). ${LOG:+See $LOG}${UNIT_FILE:+See journalctl --user -u $UNIT}"
    fi

    # Keep the live version and the one before it, for a manual rollback.
    local keep_prev="${previous#versions/}"
    for dir in "${INSTALL_DIR:?}"/versions/*; do
        [ -d "$dir" ] || continue
        local name="${dir##*/}"
        [ "$name" = "$TAG" ] || [ "$name" = "$keep_prev" ] || rm -rf -- "$dir"
    done

    if [ "$OS" = linux ] && ! loginctl show-user "$USER" -p Linger 2>/dev/null | grep -q yes; then
        warn "To start at boot without logging in, run: sudo loginctl enable-linger $USER"
    fi
}

main() {
    TAG="" FORCE=0 UNINSTALL=0 URLS="" CONFIG="" INSTALL_DIR="" LEGACY="" RETIRED=""
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
    find_legacy_agents
    read_existing_settings
    INSTALL_DIR="${INSTALL_DIR:-$DEFAULT_DIR}"
    URLS="${URLS:-http://127.0.0.1:8752}"
    CONFIG="${CONFIG:-$HOME/.config/propresenter-utilities/config.json}"

    if [ "$UNINSTALL" = 1 ]; then uninstall; else install; fi
}

# Everything is inside functions, so a truncated download through curl | bash runs nothing.
main "$@"
