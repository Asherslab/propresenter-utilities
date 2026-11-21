# Configuration

Everything the app remembers — profiles, rules, actions and the ProPresenter connection — lives in a
single JSON file. The UI is the intended way to edit it, but the format is plain and stable enough to
hand-edit, diff and keep in version control.

## Where the file lives

| Platform | Default path |
| --- | --- |
| macOS | `~/Library/Application Support/propresenter-utilities/config.json` |
| Linux | `~/.config/propresenter-utilities/config.json` (or `$XDG_CONFIG_HOME`) |
| Windows | `%APPDATA%\propresenter-utilities\config.json` |

Override it with the `PROPRESENTER_UTILITIES_CONFIG` environment variable or `--config <path>`. The
Settings page shows the path currently in use.

Writes are atomic — the file is written to a temporary name and moved into place — so a service that
is killed mid-write cannot leave you with a half-written set of cues.

If the file cannot be read the app logs the problem and starts with an empty default profile rather
than refusing to start; fix the file and restart.

## Example

```json
{
  "version": 1,
  "connection": {
    "host": "localhost",
    "port": 54937
  },
  "signals": {
    "rossTalkEnabled": true,
    "rossTalkPort": 7788
  },
  "lights": {
    "enabled": true,
    "host": "192.168.1.50",
    "channels": "0,1,2,3"
  },
  "triggersArmed": true,
  "activeProfileId": "4ba4234c-a8a5-4633-8178-8abead7572c4",
  "profiles": [
    {
      "id": "4ba4234c-a8a5-4633-8178-8abead7572c4",
      "name": "Sunday AM",
      "notes": "Pre-service loop into the welcome video",
      "rules": [
        {
          "id": "2a333ef9-4178-4263-850e-6092ce78c45e",
          "name": "Backing track",
          "enabled": true,
          "timer": {
            "uuid": "637EE351-0E0A-4691-86D4-D8B93DE7B518",
            "name": "PreShow Countdown"
          },
          "kind": "OnStart",
          "offset": "00:00:00",
          "targetState": "Complete",
          "actions": [
            {
              "actionId": "macro.trigger",
              "arguments": { "id": "B2F0A4C1-7E3D-4A55-9C10-1D4F8E2A6B37" }
            }
          ]
        },
        {
          "id": "8c1f0d52-96a4-4f0e-bb27-3f5c2a9d1e44",
          "name": "Countdown music",
          "enabled": true,
          "timer": {
            "uuid": "637EE351-0E0A-4691-86D4-D8B93DE7B518",
            "name": "PreShow Countdown"
          },
          "kind": "AtRemaining",
          "offset": "00:01:00",
          "targetState": "Complete",
          "actions": [
            {
              "actionId": "audio.byid.trigger",
              "arguments": { "playlist_id": "1A9C33E0-5B77-4D21-8E64-90BB2C7F4D18" }
            }
          ]
        },
        {
          "id": "f47d9b10-2c6e-4a83-9d15-7e0b3a8c5612",
          "name": "Start the service",
          "enabled": true,
          "timer": {
            "uuid": "637EE351-0E0A-4691-86D4-D8B93DE7B518",
            "name": "PreShow Countdown"
          },
          "kind": "OnEnd",
          "offset": "00:00:00",
          "targetState": "Complete",
          "actions": [
            { "actionId": "playlist.focused.next.trigger", "arguments": {} }
          ]
        }
      ]
    }
  ]
}
```

## Fields

### Root

| Field | Meaning |
| --- | --- |
| `version` | Format version. Currently `1`. |
| `connection.host` / `connection.port` | Where ProPresenter is. Changing this from the Settings page reconnects the timer stream without a restart. |
| `signals.rossTalkEnabled` / `signals.rossTalkPort` | The inbound RossTalk listener that lets a ProPresenter macro fire rules. See [SIGNALS.md](SIGNALS.md). |
| `lights.enabled` / `lights.host` | The Shelly relay that the FOH lights actions switch. See [LIGHTS.md](LIGHTS.md). |
| `lights.password` | Only if authentication is enabled on the Shelly. Stored in plain text. Omitted when unset. |
| `lights.channels` | The relay outputs an action set to `all` switches. A Pro4PM has `0`–`3`. |
| `triggersArmed` | Master switch. When `false` the dashboard still updates but nothing fires. |
| `activeProfileId` | The profile whose rules are armed. Must match one of the profiles. |
| `profiles` | All profiles, active or not. |

If `activeProfileId` does not match any profile, the first profile is activated on load. If there are
no profiles at all, an empty "Default" one is created.

### Profile

| Field | Meaning |
| --- | --- |
| `id` | GUID. Referenced by `activeProfileId`. |
| `name` | Shown in the profile switcher. |
| `notes` | Free text, for your own reference. |
| `rules` | The triggers in this profile. |

### Rule

| Field | Meaning |
| --- | --- |
| `id` | GUID, generated when the rule is created. |
| `name` | Optional label. When blank, the UI shows a description derived from the condition. |
| `enabled` | A disabled rule is never evaluated. |
| `source` | `Timer` or `Signal`. Decides which fields below apply. |
| `timer.uuid` | `Timer` rules: which ProPresenter timer this watches. **This is the match key.** |
| `timer.name` | Kept for display only, so a rule still reads sensibly if the timer is deleted. |
| `signalKey` | `Signal` rules: the signal to listen for, e.g. `GPI 1` or `webhook:start`. Matched case-insensitively. |
| `kind` | `Timer` rules: the condition — see below. Ignored by signal rules. |
| `offset` | `hh:mm:ss`. A cue point for `AtTimerValue`/`AtElapsed`/`AtRemaining`; a **delay** for signal rules. |
| `targetState` | Used by `OnStateChange` only. One of `Stopped`, `Running`, `Complete`, `Overrunning`, `Overran`. |
| `actions` | Ordered list; run top to bottom when the rule fires. |

Note `offset` means different things depending on `source`, which is why switching a rule between the
two in the UI resets it rather than reinterpreting a cue point as a delay.

A signal rule looks like this:

```json
{
  "name": "Cancel auto-advance",
  "enabled": true,
  "source": "Signal",
  "signalKey": "GPI 1",
  "offset": "00:00:00",
  "actions": [
    {
      "actionId": "transport.auto_advance.disable",
      "arguments": { "layer": "presentation" }
    }
  ]
}
```

Rules match a timer by **UUID**, so renaming a timer in ProPresenter does not break its cues. If
`timer.uuid` is empty the `timer.name` is used as a fallback, which is what makes a hand-written or
imported config usable before you know the UUIDs.

### `kind` values

| Value | Fires when |
| --- | --- |
| `OnStart` | The timer enters the running state |
| `AtElapsed` | Elapsed time crosses `offset` |
| `AtRemaining` | Remaining time crosses `offset` |
| `AtTimerValue` | The displayed clock crosses `offset`, in either direction |
| `OnEnd` | The timer enters `complete` or `overrunning`, whichever happens first |
| `OnStop` | Running → stopped, without completing |
| `OnOverran` | The timer enters `overran` |
| `OnStateChange` | The timer enters `targetState` |

### Action

| Field | Meaning |
| --- | --- |
| `actionId` | Identifier from [ACTIONS.md](ACTIONS.md). Stable across versions. |
| `arguments` | String map of parameter key to value. Keys are the parameter names in the action's endpoint path, plus any body parameter. |

Values are always strings, including numbers and booleans (`"true"`, `"60"`). An unrecognised
`actionId` is flagged in the editor and skipped at runtime rather than crashing the rule.

## Moving configuration between machines

Use **Export profiles** and **Import profiles** on the Settings page, or copy the file directly.

Import deliberately keeps the *current* machine's `connection` and `lights` blocks, since an imported
file usually comes from a different machine with a different host or port.

Object references are UUIDs from the source ProPresenter instance. Importing into a *different*
ProPresenter installation will leave those references dangling — the editor shows them as
`… (not found)` rather than silently repointing them at something else, so they are easy to spot and
fix. Between machines running the same ProPresenter document set, UUIDs match and everything carries
over.

## Editing by hand

Stop the service first. The app holds the configuration in memory and rewrites the whole file on any
change from the UI, so edits made while it is running will be overwritten.
