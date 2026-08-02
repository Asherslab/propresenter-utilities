# Signals — triggering from a ProPresenter macro

Timer triggers cover "do this at a point in a countdown". Signals cover the other half: **"do this
when a macro runs"**. Both use the same rules, the same action catalog and the same Triggers page —
a rule is simply set to be triggered by a signal rather than a timer.

The motivating case: cancelling a pending auto-advance from a macro. ProPresenter has no macro action
for that, so it has to leave the application and come back through the API.

- [Why RossTalk](#why-rosstalk)
- [Setting it up](#setting-it-up)
- [Signal keys](#signal-keys)
- [The webhook](#the-webhook)
- [If ProPresenter runs on Windows](#if-propresenter-runs-on-windows)
- [Troubleshooting](#troubleshooting)

## Why RossTalk

A ProPresenter macro can trigger looks, props, messages, stage layouts, timers, audio transport, other
macros — and **Communication** actions. Communication is the only category that reaches outside
ProPresenter, and it offers MIDI, DMX, AMP, CITP, GlobalCache, GVG100, RossTalk, Sony BVS/BVW and VDCP.
A macro cannot make an HTTP request.

Of those, **RossTalk** is the best fit by some distance:

| | RossTalk | MIDI | The video-deck protocols |
| --- | --- | --- | --- |
| Transport | Plain ASCII over TCP | MIDI port | Serial / specialised TCP |
| Needs extra hardware or drivers | No | Virtual MIDI port (IAC, loopMIDI) | Usually yes |
| Works across the network | Yes | Not without extra plumbing | Rarely |
| Dependencies in this app | None — a socket and a line reader | A native MIDI library | Protocol implementation |
| Latency | Milliseconds | Milliseconds | Varies |

This is also why the app talks RossTalk directly rather than going through Bitfocus Companion. The
usual Companion setup is *macro → MIDI → Companion → HTTP → target*; here it is just
*macro → RossTalk → this app → ProPresenter*, with one fewer piece of software to keep running.

RossTalk is a line-based text protocol: ProPresenter opens a TCP connection to an address you give it
and writes a short command such as `GPI 1\r\n` when the macro runs. This app is the server on the
other end.

## Setting it up

**1. Check the listener.** Open the **Signals** page. The RossTalk listener is on by default on port
**7788** (the protocol's conventional port, and what ProPresenter's dialog suggests). Change it if
something else on the machine already uses it.

**2. Add the device in ProPresenter.** Preferences → **Devices** → **+** → **RossTalk**. Set:

- **Address** — the machine running this app. `127.0.0.1` if that is the same machine as ProPresenter.
- **Port** — `7788`, or whatever the Signals page shows.

Click **Connect**. ProPresenter shows a green indicator, and the Signals page's client count goes to 1.

**3. Add the action to your macro.** Edit the macro and add **Communication → RossTalk → Trigger GPI**,
with a GPI number you are not otherwise using — 1, say. `Custom Control` works equally well if you
prefer; only the identity matters here, not the meaning.

**4. Run the macro.** `GPI 1` appears in the **Live signals** table within a moment, marked as matching
no rules.

**5. Attach a trigger.** Press **Attach a trigger** on that row. You land on the Triggers page with a
new rule already listening for `GPI 1`. Add an action — for the auto-advance case that is
**Transport → Cancel auto-advance**, with the layer set to `presentation` — and it is live immediately.

Press **Test fire** on the rule to run its actions without waiting for the macro.

### The auto-advance example, end to end

| | |
| --- | --- |
| ProPresenter macro | `Communication → RossTalk → Trigger GPI 1` |
| Rule triggered by | A macro / signal, `GPI 1` |
| Rule action | Transport → Cancel auto-advance, layer `presentation` |
| What happens | The macro fires, the app receives `GPI 1`, and sends `DELETE /v1/transport/presentation/auto_advance` |

## Signal keys

Everything that arrives becomes a **signal key**, and rules match on that key exactly
(case-insensitively, ignoring surrounding whitespace).

| Sent by ProPresenter | Signal key |
| --- | --- |
| Trigger GPI, number 1 | `GPI 1` |
| Custom Control, number 3 | `CC 3` |
| Custom Control, page 2 button 5 | `CC 2:5` |
| Fade to black | `FTB` |
| Key Cut / Key Trans | `KEYCUT …` / `KEYTRANS …` |
| A webhook call to `/api/signals/foo` | `webhook:foo` |

Verbs are upper-cased and internal whitespace collapsed, so `gpi 1` and `GPI    1` both become `GPI 1`
and match the same rule. Matching is exact, not prefix-based: `GPI 1` and `GPI 11` are different
signals.

Several rules can listen for the same signal; they all fire. Each can have its own optional **delay**,
so one macro can drive a small sequence — cancel the auto-advance now, start the next presentation
five seconds later.

## The webhook

Anything able to make an HTTP request can raise a signal:

```bash
curl http://127.0.0.1:8752/api/signals/start-service
```

GET and POST both work. That arrives as `webhook:start-service`. Use it for Companion, a Stream Deck,
a shell script, or another automation system.

Note there is **no authentication** on this endpoint, exactly as there is none on ProPresenter's own
API. Keep the app bound to loopback unless you are on a trusted production network — see
[INSTALL.md](INSTALL.md#network-exposure).

## If ProPresenter runs on Windows

ProPresenter only offers RossTalk devices on macOS; the Windows build supports MIDI devices only. If
ProPresenter runs on Windows you have two options:

- **Use the webhook with a bridge.** Macro → MIDI → Companion (or similar) → HTTP → `/api/signals/…`.
  This is the arrangement RossTalk removes on macOS, but it still works.
- **Use a timer as the signal.** Macros can start timers on every platform. Point the macro at a
  dedicated timer that is not displayed anywhere and attach an ordinary **When the timer starts** rule
  to it. Latency is ProPresenter's ~1s tick rather than milliseconds, and you have to reset the timer
  before it can signal again, so prefer the webhook where you can.

## Troubleshooting

**ProPresenter will not connect / no green indicator.** Check the Signals page says *Listening* and
that the port matches. If it says *Failed*, something else holds the port. If this app and
ProPresenter are on different machines, check the firewall on the machine running this app.

**The macro runs but nothing appears in Live signals.** The macro is not actually sending — confirm
the RossTalk action is in the macro itself and not on a slide you did not trigger, and that the device
is connected in ProPresenter's Devices preferences.

**The signal appears but "Rules fired" says none.** The signal arrived and no rule is listening for
that exact key. Press **Attach a trigger** on the row, or check that an existing rule's signal matches
character for character.

**The signal fires but nothing happens in ProPresenter.** Look at the **Activity** page: each action
reports success or failure with the API's error. Check the **Triggers armed** switch on the dashboard,
and that the profile holding the rule is the active one.

**A signal fires twice.** Two rules are probably listening for the same key, which is legitimate; the
Activity page names each rule that fired.
