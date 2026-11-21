# FOH lights

Switching the front-of-house lights from the same triggers that drive ProPresenter, through a Shelly
Gen2 relay such as the **Pro4PM**.

- [The four cues](#the-four-cues)
- [Setting up the Shelly](#setting-up-the-shelly)
- [Setting up the cues](#setting-up-the-cues)
- [Order: switching in batches](#order-switching-in-batches)
- [How "lights off for this video" decides the video has ended](#how-lights-off-for-this-video-decides-the-video-has-ended)
- [Failsafes](#failsafes)
- [Troubleshooting](#troubleshooting)

## The four cues

| When | Lights | Trigger | Action |
| --- | --- | --- | --- |
| Beginning-of-service countdown reaches ~15s | Off | Timer → *At a time before the end*, `00:00:15` | Turn lights on / off → **off** |
| Beginning-of-service slides finish (last slide is held) | On | Macro / signal — a macro on the last slide | Turn lights on / off → **on** |
| A video that should darken the room starts | Off | Macro / signal — a macro on that video's cue | **Lights off for this video** |
| That video ends | On | *(automatic)* | — |

The last row needs no rule. **Lights off for this video** turns the lights off *and* remembers to
turn them back on when that video ends. Lights turned off any other way stay off until something turns
them on, so the countdown cue, or a video without the macro, never brings the lights up by accident.

Which videos darken the room is decided by which cues carry the macro. A video without it leaves the
lights alone.

## Setting up the Shelly

1. Give the Shelly a fixed address (a DHCP reservation on the router is easiest).
2. In the Shelly's own web UI, set each output's **power-on default** to *ON*. A power blip then
   restores the lights instead of leaving the room dark.
3. In this app, open **Settings → FOH lights**. Tick **Enabled**, then enter the address. Enter the
   password only if you turned on authentication in the Shelly. **Outputs for "all"** is `0,1,2,3`
   for the four channels of a Pro4PM.
4. Press **Test**. It reads each output's state without switching anything.

The dashboard then shows a **FOH lights** bar with **Lights on** / **Lights off** buttons. They
always work, whatever the triggers are doing, and they also cancel a pending "back on when the video
ends".

## Setting up the cues

Macros reach this app over RossTalk. Set that up once as described in [SIGNALS.md](SIGNALS.md#setting-it-up).

**Countdown → off.** On the Triggers page add a rule triggered by **a timer**: your beginning-of-service
countdown, *At a time before the end*, `00:00:15`. Action: **FOH lights → Turn lights on / off**,
state `off`.

**End of the opening slides → on.**

1. Make a ProPresenter macro *Lights on* containing **Communication → RossTalk → Trigger GPI 10**.
2. Attach the macro to the last slide of the beginning-of-service presentation.
3. Add a rule triggered by **a macro / signal**, `GPI 10`. Action: **Turn lights on / off**, state `on`.

**Video → off, end → on.**

1. Make a macro *Lights off for video* containing **Communication → RossTalk → Trigger GPI 11**.
2. Attach the macro to the cue of every video that should darken the room.
3. Add a rule triggered by **a macro / signal**, `GPI 11`. Action: **FOH lights → Lights off for this
   video**.

Any GPI numbers work; they just need to be ones no other rule uses. **Test fire** on each rule switches
the real lights, so it's the quickest way to check the Shelly side.

## Order: switching in batches

By default every output switches together. To bring the lights up in stages, add groups under
**Settings → FOH lights → Order**, for example:

| Group | Outputs | Gap before |
| --- | --- | --- |
| Wides | `0,1` | — (first) |
| Centers | `2,3` | 0.1s |

**Turning on** goes top to bottom: Wides, then Centers 0.1s later. **Turning off** goes bottom to
top with the same gaps: Centers, then Wides 0.1s later. Each group is one `Switch.SetMany` call, so
the outputs within a group stay together. Gaps are timed from when each group is sent, so the
device's response time doesn't stretch them.

- **Every switch follows the order:** rules, the end-of-video restore, and the dashboard buttons.
- **A rule can opt out:** set the action's **Order** to *all together* for a cue that should snap
  everything at once.
- **Outputs a rule asks for that are in no group** switch with the first group.
- **Groups with none of the rule's outputs** are skipped, along with their gap.
- **An output listed in two groups** is refused when you save.
- **Gaps** run from 0 to 10 seconds.

The relay's failsafe timer (see [Failsafes](#failsafes)) runs on the device, so if it ever fires,
it brings the outputs back without the order.

## How "lights off for this video" decides the video has ended

While a lights-off-for-video is pending, the app polls `GET /v1/transport/presentation/current` and
`/time` twice a second. It does nothing at all otherwise. ProPresenter's API reports media-layer video
on its `presentation` transport layer.

1. **Finding the video.** The macro runs on the video's own cue, so the layer may still be showing the
   *previous* video for a moment. A playing video is taken as "the one" when it differs from what was
   there as the lights went off, or has just started. After 3 seconds, whatever is playing counts. That
   rule is what makes **Test fire** work mid-video.
2. **Ending.** The video has ended when any of these happens:
   - the layer empties: the operator clears it, or the video's end action is *Clear*;
   - a different item replaces it;
   - it stops at its end: end action *Stop*, or a rewind to the start.

   Pausing mid-video does **not** count. A looping video keeps the lights off until it's cleared.
3. **Lights on** after the action's **Back on after** time, 0.3 seconds by default (0–10s, fractions
   allowed). If the next cue is another lights-off video, its macro lands inside that window and the
   lights simply stay off, with no flash. Lengthen it if a cue's next video takes longer to start.

If ProPresenter doesn't answer a poll, that reading is skipped, never taken as the video ending.

If no video turns up within 15 seconds, the lights come back on and the Activity page says why. That
usually means the video isn't on the presentation layer; see Troubleshooting.

## Failsafes

**Lights off for this video** has a **Failsafe margin**, `0` (off) by default. When set, the relay
itself is told to switch back on at *remaining time + margin* once the video's length is known (via
`toggle_after` on the switch command). The timer runs on the Shelly, not in this app. So if this machine,
the network or ProPresenter fails mid-video, the room still comes back up. Until the video is found,
the relay timer is 15s + margin.

> **Turning the lights on early cancels the failsafe.** Every command this app sends is an explicit
> set with `on=true` or `on=false`, never a toggle. A set without `toggle_after` clears any running
> timer, so lights switched on early (by this app, or anything else using `Switch.Set` or
> `Switch.SetMany`) can't be switched off later by an old failsafe. Verified on a Pro4PM running
> firmware 2.2.0: an output turned off with `toggle_after=600` showed a 600-second timer, and a plain
> `on` removed it. Shelly's documentation doesn't say whether a physical input does the same, so if
> the Pro4PM's inputs are wired to wall switches, check that once:
>
> ```bash
> curl "http://IP/rpc/Switch.Set?id=0&on=false&toggle_after=30"
> # turn output 0 on at the wall switch, then:
> curl "http://IP/rpc/Switch.GetStatus?id=0"   # no timer_* fields = cancelled
> ```

Pausing a video for longer than the margin lets the failsafe bring the lights up. That's deliberate:
it errs on the side of light.

**Turn lights on / off** has an optional **Switch back after** for the same purpose, `0` (off) by
default. The countdown → off cue usually wants it left at `0`, since the lights are meant to stay off
until the slides end.

## Troubleshooting

**Every video brings the lights back after ~15 seconds.** No playing video was seen on the
`presentation` transport layer. Open
`http://<propresenter-host>:<port>/v1/transport/presentation/current` in a browser while the video
plays. It should show the video with `"is_playing": true`. If it doesn't, the video is playing
somewhere the API doesn't report as transport (for example, as a slide's background).

**Lights don't come back when the video ends.** Check the video's end behaviour in ProPresenter. A
video set to *Loop* never ends; one set to *Stop* or *Clear* does. The Activity page shows the
"Lights stay off until '…' ends" line when the video was found, and the reason when it ended.

**401 Unauthorized.** Authentication is on in the Shelly and the password in Settings is missing or
wrong. The user name is always `admin` on Shelly Gen2 devices.

**The outputs don't switch at the same moment.** The app switches all of them in one
`Switch.SetMany` call. On a Pro4PM that needs firmware **2.2.0 or newer**: 1.6.0 and 2.1.1 don't have
the method. Without it the app logs "has no Switch.SetMany" and falls back to one request per output.
The device serves those a couple at a time, so on older firmware the outputs can change in pairs up
to a few seconds apart. Update the firmware from the Shelly's web UI. If the outputs still differ
with `SetMany` in use, the difference is in the fixtures or their drivers, not the switching.

**One output did not switch.** The Activity page lists failures per output. Check **Outputs for
"all"** against the outputs the device actually has.
