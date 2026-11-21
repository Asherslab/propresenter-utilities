# ProPresenter Utilities

A companion service for [ProPresenter](https://renewedvision.com/propresenter/) that fires cues from
its timers — when a timer starts, at a time relative to its start or its end, when the clock passes a
value, or when it finishes — and from **ProPresenter macros**, via RossTalk.

Cross-platform (.NET 10). Runs as a background service with a browser-based control panel.

## What it does

Attach any number of triggers to any ProPresenter timer. Each trigger runs an ordered list of
ProPresenter actions: firing a macro, starting an audio playlist, advancing to the next presentation
in a playlist, changing a look, showing a message, and around 120 others.

A trigger can also be driven by a **macro** instead of a timer, which lets a macro do things
ProPresenter has no macro action for — cancelling a pending auto-advance being the usual example. See
[docs/SIGNALS.md](docs/SIGNALS.md).

The motivating example, which is what the built-in "Default" profile is shaped for:

> A presentation loops with images. When the countdown timer starts, a backing-track playlist starts
> via macro. At 60 seconds remaining, a countdown-music playlist starts. At zero, ProPresenter
> advances to the next *presentation* in the playlist.

That is three triggers on one timer:

| Fires | Action |
| --- | --- |
| When the timer starts | Trigger macro → *Backing Track* |
| 00:01:00 before the end | Trigger macro → *Countdown Music* |
| When the timer ends | Next presentation in focused playlist |

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build.

1. In ProPresenter, open **Preferences → Network**, enable the network link, and note the port
   (54937 by default).
2. Run the app:

```bash
dotnet run --project src/ProPresenter.App
```

3. Open <http://127.0.0.1:8752>.

The **Settings** page has host/port if ProPresenter is not on this machine or not on 54937.

For running it automatically at login or boot, see [docs/INSTALL.md](docs/INSTALL.md).

## The UI

- **Dashboard** — live state of every timer, the cues armed against each with a countdown to the next
  one, transport buttons, the active-profile switch, and the master arm switch. Cue names link
  straight to their rule.
- **Triggers** — profiles and rules. Every rule has a **Test fire** button that runs its actions
  immediately, so you can prove a cue works without waiting for a service.
- **Timers** — create, edit and delete ProPresenter's timers.
- **Signals** — the RossTalk listener, a live feed of incoming signals, and one-click **Attach a
  trigger** for whichever signal your macro just sent.
- **Activity** — everything that has fired, with timestamps and per-action success or failure.
- **Settings** — connection, cached ProPresenter lists, config file location, import/export.

## FOH lights

Any trigger can also switch the front-of-house lights through a Shelly relay (a Pro4PM, for example),
including a **Lights off for this video** action that brings them back up by itself when the video
ends. [docs/LIGHTS.md](docs/LIGHTS.md) has the setup and the four cues it was built for.

## Trigger types

A rule is triggered either by **a timer** or by **a macro / signal**.

### Macro / signal

Fires when a named signal arrives, with an optional delay. A ProPresenter macro raises one by sending
a RossTalk command (`Communication → RossTalk → Trigger GPI`); anything else can use the HTTP webhook
at `/api/signals/<name>`. Full walkthrough in [docs/SIGNALS.md](docs/SIGNALS.md).

### Timer

| Type | Fires when |
| --- | --- |
| When the timer starts | The timer enters the running state |
| At a time after the start | Elapsed time crosses the value you set |
| At a time before the end | Remaining time crosses the value you set |
| When the clock reaches a value | The displayed clock crosses the value, counting either direction |
| When the timer ends | The timer enters `complete` **or** `overrunning`, whichever comes first |
| When the timer is stopped early | Running → stopped without completing |
| When overrun finishes | The timer enters `overran` |
| When the timer enters a state | Any specific state, for cases the above do not cover |

"When the timer ends" deliberately covers both terminal transitions, so a rule behaves the same
whether or not the timer allows overrun.

### Behaviour worth knowing

ProPresenter streams timer state about once a second at whole-second resolution, so that is the
accuracy of a time-based cue.

- **Cues are edge-triggered on crossing, not on equality.** If a tick is late or dropped and the clock
  jumps from 1:05 to 0:55, a cue at 1:00 still fires on that tick rather than being skipped.
- **Restarting mid-service does not replay cues.** On connect, each timer's current position is taken
  as a baseline; points already passed are treated as done, and only the ones still ahead are armed.
- **Resetting a timer re-arms its cues.** A backwards jump is recognised as a reset rather than as the
  clock running through cue points in reverse. Adding time with the increment buttons has the same
  effect, so a cue pushed back into the future will fire again.
- **Timers with no defined end** (an elapsed timer without an end time, before it has started) cannot
  satisfy "before the end" rules, and those rules stay silent rather than guessing.

## Profiles

Rules live in named profiles — "Sunday AM", "Youth Night", and so on — and exactly one is active at a
time. Switch profiles from the dashboard. The **Triggers armed** switch disables all firing without
changing any configuration, which is what you want while rehearsing.

Configuration is a single JSON file; see [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

## Actions

Around 120 ProPresenter operations are available as trigger actions, covering macros, playlists,
presentations, slides, audio, media, announcements, clears, looks, props, messages, stage screens,
timers, transport, capture, video inputs and screen enable/disable.

[docs/ACTIONS.md](docs/ACTIONS.md) lists what is covered and, explicitly, what is not and why.

## Project layout

| Project | Contents |
| --- | --- |
| `src/ProPresenter.Api` | Typed API client, the chunked timer stream reader, the action catalog |
| `src/ProPresenter.Triggers` | Rule model, the trigger evaluator, the engine, config persistence |
| `src/ProPresenter.App` | ASP.NET Core host and Blazor Server UI |
| `tests/ProPresenter.Triggers.Tests` | Evaluator, stream framing, model and catalog tests |

```bash
dotnet test
```

The catalog tests validate every action's path and method against `swagger.json`, so a mistyped
endpoint fails the build rather than a service.

## Security

Neither ProPresenter's API nor this UI is authenticated. The app therefore binds to `127.0.0.1` by
default, so only this machine can reach it. If you change that, anyone who can reach the port can fire
your cues — put it on a trusted network only. See [docs/INSTALL.md](docs/INSTALL.md#network-exposure).

## Notes and limitations

- ProPresenter's REST responses are eventually consistent: a timer you just created, renamed or
  deleted can keep appearing in `/v1/timers` for a short while. The Timers page hides a timer as soon
  as you delete it and retries the read, but ProPresenter's own list may lag.
- ProPresenter only offers RossTalk devices on macOS. If ProPresenter runs on Windows, use the webhook
  with a bridge, or a timer as the signal — see [docs/SIGNALS.md](docs/SIGNALS.md#if-propresenter-runs-on-windows).
- Wall-clock triggers ("at 10:29:30 regardless of any timer") are not implemented. Use ProPresenter's
  own count-down-to-time timer, which this app treats like any other.
- Only ProPresenter's own timers are watched; the app does not define timers of its own.
