# Actions

Every ProPresenter operation a trigger can perform, and — explicitly — every operation that is not
exposed, with the reason why.

The catalog contains **121 ProPresenter actions** covering **127 of the 242 operations** in
ProPresenter's OpenAPI document, plus two [FOH lights](#foh-lights) actions that switch a Shelly relay. Every operation that *changes ProPresenter's live output state* is available; what is left
out is reads, thumbnails, and object management, for the reasons given at the bottom.

Each action's identifier is stored in your profile file and is treated as a stable contract, so
upgrades will not silently repoint a cue at a different operation.

> ProPresenter actions are validated against `swagger.json` by the test suite: every path and method in the catalog
> must exist in the spec, every `{placeholder}` must have a matching parameter, and every choice
> parameter must have a valid default. A mistyped endpoint fails `dotnet test`.

## Parameters

Parameters that identify a ProPresenter object (a macro, a playlist, a look) are dropdowns populated
live from ProPresenter and are stored **by UUID**, so renaming an object in ProPresenter does not break
a cue that points at it. When ProPresenter is unreachable the dropdown falls back to a text box, so
rules can still be written offline; anywhere a name, index or UUID is accepted, all three work.

## Available actions

### Macro

| Action | Endpoint |
| --- | --- |
| Trigger macro | `GET /v1/macro/{id}/trigger` |

### General

| Action | Endpoint |
| --- | --- |
| Trigger next cue | `GET /v1/trigger/next` |
| Trigger previous cue | `GET /v1/trigger/previous` |
| Next audio item | `GET /v1/trigger/audio/next` |
| Previous audio item | `GET /v1/trigger/audio/previous` |
| Next media item | `GET /v1/trigger/media/next` |
| Previous media item | `GET /v1/trigger/media/previous` |
| Find my mouse | `GET /v1/find_my_mouse` |

### Playlist

| Action | Endpoint |
| --- | --- |
| Next presentation in focused playlist | `GET /v1/playlist/focused/next/trigger` |
| Previous presentation in focused playlist | `GET /v1/playlist/focused/previous/trigger` |
| Retrigger focused playlist | `GET /v1/playlist/focused/trigger` |
| Trigger item in focused playlist | `GET /v1/playlist/focused/{index}/trigger` |
| Trigger cue of item in focused playlist | `GET /v1/playlist/focused/{index}/{cue_index}/trigger` |
| Focused playlist item timeline | `GET /v1/playlist/focused/{index}/timeline/{operation}` |
| Focus next playlist | `GET /v1/playlist/next/focus` |
| Focus previous playlist | `GET /v1/playlist/previous/focus` |
| Focus a playlist | `GET /v1/playlist/{playlist_id}/focus` |
| Trigger a playlist | `GET /v1/playlist/{playlist_id}/trigger` |
| Next item in a playlist | `GET /v1/playlist/{playlist_id}/next/trigger` |
| Previous item in a playlist | `GET /v1/playlist/{playlist_id}/previous/trigger` |
| Trigger item in a playlist | `GET /v1/playlist/{playlist_id}/{index}/trigger` |
| Trigger cue of item in a playlist | `GET /v1/playlist/{playlist_id}/{index}/{cue_index}/trigger` |
| Retrigger active presentation playlist | `GET /v1/playlist/active/presentation/trigger` |
| Focus active presentation playlist | `GET /v1/playlist/active/presentation/focus` |
| Trigger item in active presentation playlist | `GET /v1/playlist/active/presentation/{index}/trigger` |
| Trigger cue in active presentation playlist | `GET /v1/playlist/active/presentation/{index}/{cue_index}/trigger` |
| Active presentation playlist item timeline | `GET /v1/playlist/active/presentation/{index}/timeline/{operation}` |
| Retrigger active announcement playlist | `GET /v1/playlist/active/announcement/trigger` |
| Focus active announcement playlist | `GET /v1/playlist/active/announcement/focus` |
| Trigger item in active announcement playlist | `GET /v1/playlist/active/announcement/{index}/trigger` |
| Trigger cue in active announcement playlist | `GET /v1/playlist/active/announcement/{index}/{cue_index}/trigger` |
| Active announcement playlist item timeline | `GET /v1/playlist/active/announcement/{index}/timeline/{operation}` |
| Playlist item timeline | `GET /v1/playlist/{id}/{index}/timeline/{operation}` |

### Presentation

| Action | Endpoint |
| --- | --- |
| Next slide (active presentation) | `GET /v1/presentation/active/next/trigger` |
| Previous slide (active presentation) | `GET /v1/presentation/active/previous/trigger` |
| Restart active presentation | `GET /v1/presentation/active/trigger` |
| Go to slide (active presentation) | `GET /v1/presentation/active/{index}/trigger` |
| Trigger group (active presentation) | `GET /v1/presentation/active/group/{group_id}/trigger` |
| Active presentation timeline | `GET /v1/presentation/active/timeline/{operation}` |
| Focus active presentation | `GET /v1/presentation/active/focus` |
| Next slide (focused presentation) | `GET /v1/presentation/focused/next/trigger` |
| Previous slide (focused presentation) | `GET /v1/presentation/focused/previous/trigger` |
| Restart focused presentation | `GET /v1/presentation/focused/trigger` |
| Go to slide (focused presentation) | `GET /v1/presentation/focused/{index}/trigger` |
| Trigger group (focused presentation) | `GET /v1/presentation/focused/group/{group_id}/trigger` |
| Focused presentation timeline | `GET /v1/presentation/focused/timeline/{operation}` |
| Focus next presentation | `GET /v1/presentation/next/focus` |
| Focus previous presentation | `GET /v1/presentation/previous/focus` |
| Trigger presentation by UUID | `GET /v1/presentation/{uuid}/trigger` |
| Go to slide of presentation by UUID | `GET /v1/presentation/{uuid}/{index}/trigger` |
| Next slide of presentation by UUID | `GET /v1/presentation/{uuid}/next/trigger` |
| Previous slide of presentation by UUID | `GET /v1/presentation/{uuid}/previous/trigger` |
| Trigger group of presentation by UUID | `GET /v1/presentation/{uuid}/group/{group_id}/trigger` |
| Focus presentation by UUID | `GET /v1/presentation/{uuid}/focus` |
| Timeline of presentation by UUID | `GET /v1/presentation/{uuid}/timeline/{operation}` |
| Trigger library presentation | `GET /v1/library/{library_id}/{presentation_id}/trigger` |
| Trigger slide of library presentation | `GET /v1/library/{library_id}/{presentation_id}/{index}/trigger` |

### Announcement

| Action | Endpoint |
| --- | --- |
| Restart active announcement | `GET /v1/announcement/active/trigger` |
| Next announcement cue | `GET /v1/announcement/active/next/trigger` |
| Previous announcement cue | `GET /v1/announcement/active/previous/trigger` |
| Go to announcement cue | `GET /v1/announcement/active/{index}/trigger` |
| Focus active announcement | `GET /v1/announcement/active/focus` |
| Announcement timeline | `GET /v1/announcement/active/timeline/{operation}` |

### Audio

| Action | Endpoint |
| --- | --- |
| Next item in active audio playlist | `GET /v1/audio/playlist/active/next/trigger` |
| Previous item in active audio playlist | `GET /v1/audio/playlist/active/previous/trigger` |
| Restart active audio playlist | `GET /v1/audio/playlist/active/trigger` |
| Trigger item in active audio playlist | `GET /v1/audio/playlist/active/{id}/trigger` |
| Next item in focused audio playlist | `GET /v1/audio/playlist/focused/next/trigger` |
| Previous item in focused audio playlist | `GET /v1/audio/playlist/focused/previous/trigger` |
| Restart focused audio playlist | `GET /v1/audio/playlist/focused/trigger` |
| Start an audio playlist | `GET /v1/audio/playlist/{playlist_id}/trigger` |
| Focus an audio playlist | `GET /v1/audio/playlist/{playlist_id}/focus` |
| Next item in an audio playlist | `GET /v1/audio/playlist/{playlist_id}/next/trigger` |
| Previous item in an audio playlist | `GET /v1/audio/playlist/{playlist_id}/previous/trigger` |
| Trigger item in an audio playlist | `GET /v1/audio/playlist/{playlist_id}/{id}/trigger` |
| Trigger item in focused audio playlist | `GET /v1/audio/playlist/focused/{id}/trigger` |
| Focus active audio playlist | `GET /v1/audio/playlist/active/focus` |
| Focus next audio playlist | `GET /v1/audio/playlist/next/focus` |
| Focus previous audio playlist | `GET /v1/audio/playlist/previous/focus` |

### Media

| Action | Endpoint |
| --- | --- |
| Next item in active media playlist | `GET /v1/media/playlist/active/next/trigger` |
| Previous item in active media playlist | `GET /v1/media/playlist/active/previous/trigger` |
| Restart active media playlist | `GET /v1/media/playlist/active/trigger` |
| Trigger item in active media playlist | `GET /v1/media/playlist/active/{media_id}/trigger` |
| Next item in focused media playlist | `GET /v1/media/playlist/focused/next/trigger` |
| Previous item in focused media playlist | `GET /v1/media/playlist/focused/previous/trigger` |
| Restart focused media playlist | `GET /v1/media/playlist/focused/trigger` |
| Start a media playlist | `GET /v1/media/playlist/{playlist_id}/trigger` |
| Focus a media playlist | `GET /v1/media/playlist/{playlist_id}/focus` |
| Next item in a media playlist | `GET /v1/media/playlist/{playlist_id}/next/trigger` |
| Previous item in a media playlist | `GET /v1/media/playlist/{playlist_id}/previous/trigger` |
| Trigger item in a media playlist | `GET /v1/media/playlist/{playlist_id}/{media_id}/trigger` |
| Trigger item in focused media playlist | `GET /v1/media/playlist/focused/{media_id}/trigger` |
| Focus active media playlist | `GET /v1/media/playlist/active/focus` |
| Focus next media playlist | `GET /v1/media/playlist/next/focus` |
| Focus previous media playlist | `GET /v1/media/playlist/previous/focus` |

### Clear

| Action | Endpoint |
| --- | --- |
| Trigger clear group | `GET /v1/clear/group/{id}/trigger` |
| Clear a layer | `GET /v1/clear/layer/{layer}` |

### Look

| Action | Endpoint |
| --- | --- |
| Trigger look | `GET /v1/look/{id}/trigger` |

### Prop

| Action | Endpoint |
| --- | --- |
| Show prop | `GET /v1/prop/{id}/trigger` |
| Clear prop | `GET /v1/prop/{id}/clear` |
| Pause prop auto-clear | `GET /v1/prop/{id}/auto_clear/pause` |
| Resume prop auto-clear | `GET /v1/prop/{id}/auto_clear/resume` |

### Message

| Action | Endpoint |
| --- | --- |
| Show message | `POST /v1/message/{id}/trigger` |
| Clear message | `GET /v1/message/{id}/clear` |

### Stage

| Action | Endpoint |
| --- | --- |
| Set stage screen layout | `GET /v1/stage/screen/{id}/layout/{layout_id}` |
| Show stage message | `PUT /v1/stage/message` |
| Clear stage message | `DELETE /v1/stage/message` |

### Timer

| Action | Endpoint |
| --- | --- |
| Start / stop / reset a timer | `GET /v1/timer/{id}/{operation}` |
| Start / stop / reset all timers | `GET /v1/timers/{operation}` |
| Add time to a running timer | `GET /v1/timer/{id}/increment/{time}` |

### Transport

| Action | Endpoint |
| --- | --- |
| Play layer | `GET /v1/transport/{layer}/play` |
| Pause layer | `GET /v1/transport/{layer}/pause` |
| Skip to end of layer | `GET /v1/transport/{layer}/go_to_end` |
| Skip forward | `GET /v1/transport/{layer}/skip_forward/{time}` |
| Skip backward | `GET /v1/transport/{layer}/skip_backward/{time}` |
| Seek layer to a time | `PUT /v1/transport/{layer}/time` |
| Cancel auto-advance | `DELETE /v1/transport/{layer}/auto_advance` |

### Video input

| Action | Endpoint |
| --- | --- |
| Trigger video input | `GET /v1/video_inputs/{id}/trigger` |

### Capture

| Action | Endpoint |
| --- | --- |
| Start / stop capture | `GET /v1/capture/{operation}` |

### Screens

| Action | Endpoint |
| --- | --- |
| Enable / disable audience screens | `PUT /v1/status/audience_screens` |
| Enable / disable stage screens | `PUT /v1/status/stage_screens` |

### FOH lights

These two are not ProPresenter calls. They switch the Shelly relay set up under **Settings → FOH
lights**, using its `Switch.Set` RPC. See [LIGHTS.md](LIGHTS.md).

| Action | What it does |
| --- | --- |
| Turn lights on / off | Switches the chosen outputs, optionally telling the relay to flip back by itself after a number of seconds. Cancels a pending "lights off for this video". |
| Lights off for this video | Turns the lights off, then back on when the video on the presentation transport layer ends. |

## Not exposed

The following operations exist in the API but are deliberately not offered as trigger actions.
None of them change what the audience sees.

**Creating, editing and deleting ProPresenter objects** (30 operations)

| Endpoint | |
| --- | --- |
| `DELETE /v1/clear/group/{id}` | Deletes the specified clear group. |
| `POST /v1/clear/groups` | Create a clear group with the details specified. |
| `PUT /v1/clear/group/{id}` | Sets the details of the specified clear group. |
| `DELETE /v1/look/{id}` | Deletes the specified audience look from the saved looks. |
| `POST /v1/looks` | Creates a new audience look with the specified details. |
| `PUT /v1/look/current` | Sets the details of the currently live audience look. |
| `PUT /v1/look/{id}` | Sets the details of the specified audience look. |
| `DELETE /v1/macro/{id}` | Deletes the specified macro. |
| `DELETE /v1/macro_collection/{id}` | Deletes the specified macro collection and any macros contained within |
| `POST /v1/macro_collections` | Creates a new macro collection with the specified name |
| `PUT /v1/macro/{id}` | Sets the details of the specified macro. |
| `PUT /v1/macro_collection/{id}` | Sets the details of the specified macro collection. |
| `DELETE /v1/message/{id}` | Deletes the specified message. |
| `POST /v1/messages` | Creates a new message with specified details. |
| `PUT /v1/message/{id}` | Sets the details of the specified message. |
| `POST /v1/playlist/{playlist_id}` | Creates a playlist with the specified details underneath the specified |
| `POST /v1/playlists` | Creates a playlist with the specified details. |
| `PUT /v1/playlist/{playlist_id}` | Sets the contents of the specified playlist. |
| `DELETE /v1/prop/{id}` | Deletes the specified prop. |
| `DELETE /v1/prop_collection/{id}` | Deletes the specified prop collection and any props contained within i |
| `POST /v1/prop_collections` | Creates a new prop collection with the specified name |
| `PUT /v1/prop/{id}` | Sets the details of the specified prop. |
| `PUT /v1/prop_collection/{id}` | Sets the details of the specified prop collection. |
| `DELETE /v1/stage/layout/{id}` | Deletes the specified stage layout. |
| `PUT /v1/stage/layout_map` | Sets the specified stage message to the corresponding stage screens. |
| `POST /v1/status/updates` | Aggregates the data from one or more streaming endpoints into a single |
| `PUT /v1/theme/{id}/slides/{theme_slide}` | Sets the details of the specified theme slide within the specified the |
| `DELETE /v1/timer/{id}` | Deletes the specified timer. |
| `POST /v1/timers` | Creates a new timer with the specified details. |
| `PUT /v1/timer/{id}` | Sets the details of the specified timer. |

**Thumbnails and icons** (13 operations)

| Endpoint | |
| --- | --- |
| `GET /v1/clear/group/{id}/icon` | Requests the image data for the icon of the specified clear group. |
| `PUT /v1/clear/group/{id}/icon` | Sets the custom icon of the specified clear group. |
| `GET /v1/macro/{id}/icon` | Requests the image data for the icon of the specified macro. |
| `PUT /v1/macro/{id}/icon` | Sets the custom icon of the specified macro. |
| `GET /v1/mask/{id}/thumbnail` | Requests a thumbnail image of the specified mask at the given quality  |
| `GET /v1/media/{uuid}/thumbnail` | Requests a thumbnail image of the specified media item at the given qu |
| `GET /v1/playlist/active/announcement/{index}/thumbnail/{cue_index}` | Requests a thumbnail image for the specified item in the currently act |
| `GET /v1/playlist/active/presentation/{index}/thumbnail/{cue_index}` | Requests a thumbnail image for the specified item in the currently act |
| `GET /v1/playlist/{playlist_id}/{index}/thumbnail/{cue_index}` | Requests a thumbnail image for the specified item in the specified pla |
| `GET /v1/presentation/{uuid}/thumbnail/{index}` | Requests a thumbnail image of the specified cue inside the specified p |
| `GET /v1/prop/{id}/thumbnail` | Requests a thumbnail image of the specified prop at the given quality  |
| `GET /v1/stage/layout/{id}/thumbnail` | Requests a thumbnail image of the specified stage layout at the given  |
| `GET /v1/theme/{id}/slides/{theme_slide}/thumbnail` | Requests a thumbnail image of the specified theme slide at the given q |

**Read-only queries** (72 operations)

| Endpoint | |
| --- | --- |
| `GET /v1/announcement/active` | Requests the currently active announcement presentation. |
| `GET /v1/announcement/active/timeline` | Requests the current state of the active announcement timeline. |
| `GET /v1/announcement/slide_index` | Requests the index of the current slide/cue within the currently activ |
| `GET /v1/audio/playlist/active` | Requests the currently active audio playlist |
| `GET /v1/audio/playlist/focused` | Requests the currently focused audio playlist |
| `GET /v1/audio/playlist/{playlist_id}` | Requests a list of all the audio items in the specified audio playlist |
| `GET /v1/audio/playlist/{playlist_id}/updates` | Requests a chunked data update every time the specified audio playlist |
| `GET /v1/audio/playlists` | Requests a list with all the configured audio playlists. |
| `GET /v1/capture/encodings/{type}` | Requests a list of all available capture modes for the capture type (d |
| `GET /v1/capture/settings` | Requests the current capture settings. |
| `GET /v1/capture/status` | Requests the current capture status and capture time. |
| `GET /v1/clear/group/{id}` | Requests the details of the specified clear group. |
| `GET /v1/clear/groups` | Requests a list of all the configured clear groups. |
| `GET /v1/groups` | Requests a list of all the configured global groups. |
| `GET /v1/libraries` | Requests a list of all the configured libraries. |
| `GET /v1/library/{library_id}` | Requests an array of all items in the specified library. |
| `GET /v1/look/current` | Requests the details of the currently live audience look. |
| `GET /v1/look/{id}` | Requests the details of the specified audience look. |
| `GET /v1/looks` | Requests a list of all configured audience looks, except the live look |
| `GET /v1/macro/{id}` | Requests the details of the specified macro. |
| `GET /v1/macro_collection/{id}` | Requests a specific collection of macros |
| `GET /v1/macro_collections` | Requests a list of all collections and their contents |
| `GET /v1/macros` | Requests a list of all the configured macros. |
| `GET /v1/mask/{id}` | Requests the details of the specified mask. |
| `GET /v1/masks` | Requests a list of all configured masks. |
| `GET /v1/media/playlist/active` | Returns the identifier of the currently active media playlist. |
| `GET /v1/media/playlist/focused` | Returns the identifier of the currently focused media playlist |
| `GET /v1/media/playlist/{playlist_id}` | Requests a list of all the media items in the specified media playlist |
| `GET /v1/media/playlist/{playlist_id}/updates` | Requests a chunked data update every time the specified media playlist |
| `GET /v1/media/playlists` | Requests a list of all the configured media playlists. |
| `GET /v1/message/{id}` | Requests the details of the specified message. |
| `GET /v1/messages` | Requests a list of all configured messages. |
| `GET /v1/playlist/active` | Requests the details of the active playlist. |
| `GET /v1/playlist/active/announcement/{index}/timeline` | Requests the current state of the timeline for the specified item in t |
| `GET /v1/playlist/active/presentation/{index}/timeline` | Requests the current state of the timeline for the specified item in t |
| `GET /v1/playlist/focused` | Requests the details of the currently focused playlist. |
| `GET /v1/playlist/focused/next` | Returns the playlist item that will be triggered by /v1/playlist/focus |
| `GET /v1/playlist/focused/previous` | Returns the playlist item that will be triggered by /v1/playlist/focus |
| `GET /v1/playlist/focused/{index}/timeline` | Requests the current state of the timeline for the currently focused p |
| `GET /v1/playlist/{playlist_id}` | Requests a list of the items in the specified playlist. |
| `GET /v1/playlist/{playlist_id}/updates` | Requests a chunked data update every time the specified playlist chang |
| `GET /v1/playlists` | Requests a list of all configured playlists. |
| `GET /v1/presentation/active` | Requests the details of the currently active presentation. |
| `GET /v1/presentation/active/timeline` | Requests the current state of the timeline for the currently active pr |
| `GET /v1/presentation/chord_chart` | Requests the current chord chart image (if available) at the given qua |
| `GET /v1/presentation/chord_chart/updates` | Requests a chunked data update every time the chord chart changes. |
| `GET /v1/presentation/focused` | Gets the currently focused presentation in the main UI. |
| `GET /v1/presentation/focused/timeline` | Requests the current state of the timeline for the currently focused p |
| `GET /v1/presentation/slide_index` | Requests the index of the current slide/cue within the currently activ |
| `GET /v1/presentation/{uuid}` | Requests the details of the specified presentation. |
| `GET /v1/prop/{id}` | Requests the details of the specified prop. |
| `GET /v1/prop_collection/{id}` | Requests a specific collection of props |
| `GET /v1/prop_collections` | Requests a list of all collections and their contents |
| `GET /v1/props` | Gets a list of all the props. |
| `GET /v1/stage/layout_map` | Requests the currently selected stage layout for each configured stage |
| `GET /v1/stage/layouts` | Requests a list of the configured stage layouts. |
| `GET /v1/stage/screen/{id}/layout` | Requests the current stage layout for the specified stage screen. |
| `GET /v1/stage/screens` | Requests a list of the configured stage screens. |
| `GET /v1/status/layers` | Requests the status of all available layers. |
| `GET /v1/status/screens` | Requests the details of all configured screens. |
| `GET /v1/status/slide` | Requests the current/next slide text and image UUIDs. |
| `GET /version` | Requests the general information about the currently active ProPresent |
| `GET /v1/theme/{id}` | Requests the details of the theme and theme slides. |
| `GET /v1/theme/{id}/slides/{theme_slide}` | Requests the details of the specified theme slide within the specified |
| `GET /v1/themes` | Requests a list of all configured themes and theme slides. |
| `GET /v1/timer/system_time` | Requests the current system time. |
| `GET /v1/timer/video_countdown` | Requests the current value of the video countdown timer. |
| `GET /v1/timer/{id}` | Requests the details of the specified timer. |
| `GET /v1/timers` | Requests the details for all configured timers. |
| `GET /v1/timers/current` | Requests the current time for all configured timers. |
| `GET /v1/transport/{layer}/current` | Requests the details of the currently playing content for the specifie |
| `GET /v1/video_inputs` | Requests the contents of the video inputs playlist. |

### Why these are excluded

**Creating, editing and deleting ProPresenter objects.** Building macros, playlists, looks, props,
messages, themes and clear groups is authoring work, and doing it from a timer cue mid-service is far
more likely to be a mistake than an intention. Timers are the exception: they are managed from the
**Timers** page, because a companion app for timer triggers that cannot create a timer would be
awkward. `POST /v1/timers`, `PUT /v1/timer/{id}` and `DELETE /v1/timer/{id}` are therefore used by the
app, just not as trigger actions.

**Thumbnails and icons.** Image endpoints. There is nothing for a cue to do with them, and the UI does
not currently show slide previews.

**Read-only queries.** Nothing to trigger. Several are used internally: `/v1/timers` and
`/v1/timers/current` drive the engine, `version` is the connection test, and the list endpoints
populate the action dropdowns. `POST /v1/status/updates` is an aggregator that exists to work around
the browser's per-page connection limit; this app is a server and keeps a single stream open, so it
has no use for it.

### Deliberate functional gaps

- **Wall-clock triggers.** There is no "fire at 10:29:30 regardless of any timer". ProPresenter's own
  count-down-to-time timer covers the same ground and this app treats it like any other timer.
- **A raw API escape hatch.** No action lets you type an arbitrary path and body. Everything is a
  named, validated entry in this catalog. If something you need is missing, adding it is a few lines
  in `ActionCatalog.cs` and the tests will confirm the endpoint exists.
