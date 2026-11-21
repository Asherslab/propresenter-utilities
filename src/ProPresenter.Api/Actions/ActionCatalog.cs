namespace ProPresenter.Api.Actions;

/// <summary>
/// Every ProPresenter operation a timer trigger can perform.
/// </summary>
/// <remarks>
/// The catalog deliberately covers the state-changing surface of the API and omits reads, thumbnails,
/// icons and object CRUD — see <c>docs/ACTIONS.md</c> for the full list of what is excluded and why.
/// Action <see cref="ActionDefinition.Id"/> values are persisted in profile files, so they are part of
/// the on-disk contract and must not be renamed.
/// </remarks>
public static class ActionCatalog
{
    private static readonly string[] TimelineOperations = ["play", "pause", "rewind"];
    private static readonly string[] TransportLayers = ["presentation", "announcement", "audio"];
    private static readonly string[] ClearableLayers =
        ["audio", "props", "messages", "announcements", "slide", "media", "video_input"];
    private static readonly string[] TimerOperations = ["start", "stop", "reset"];
    private static readonly string[] CaptureOperations = ["start", "stop"];
    private static readonly string[] LightStates = ["off", "on"];
    private static readonly string[] LightOrders = ["in order", "all together"];

    private static ActionParameter Index(string key = "index", string label = "Item index", string? help = null) =>
        new(key, label, ParameterKind.Integer, help ?? "Zero-based index.", "0");

    public static IReadOnlyList<ActionDefinition> All { get; } = Build();

    private static readonly Dictionary<string, ActionDefinition> ById =
        All.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public static ActionDefinition? Find(string id) => ById.GetValueOrDefault(id);

    public static IEnumerable<IGrouping<string, ActionDefinition>> ByCategory() =>
        All.GroupBy(a => a.Category);

    private static List<ActionDefinition> Build()
    {
        var a = new List<ActionDefinition>();

        void Add(
            string id,
            string category,
            string name,
            string description,
            string method,
            string path,
            IReadOnlyList<ActionParameter>? parameters = null,
            BodySource bodySource = BodySource.None,
            string? bodyKey = null) =>
            a.Add(new ActionDefinition(id, category, name, description, method, path, parameters ?? [], bodySource, bodyKey));

        // ------------------------------------------------------------ Macro
        Add("macro.trigger", "Macro", "Trigger macro",
            "Runs a ProPresenter macro. The most flexible action: anything ProPresenter can do in a macro.",
            "GET", "v1/macro/{id}/trigger",
            [new ActionParameter("id", "Macro", ParameterKind.MacroId)]);

        // ---------------------------------------------------------- General
        Add("trigger.next", "General", "Trigger next cue",
            "Advances the currently active presentation to its next cue.", "GET", "v1/trigger/next");
        Add("trigger.previous", "General", "Trigger previous cue",
            "Steps the currently active presentation back one cue.", "GET", "v1/trigger/previous");
        Add("trigger.audio.next", "General", "Next audio item",
            "Triggers the next item in the active audio playlist.", "GET", "v1/trigger/audio/next");
        Add("trigger.audio.previous", "General", "Previous audio item",
            "Triggers the previous item in the active audio playlist.", "GET", "v1/trigger/audio/previous");
        Add("trigger.media.next", "General", "Next media item",
            "Triggers the next item in the active media playlist.", "GET", "v1/trigger/media/next");
        Add("trigger.media.previous", "General", "Previous media item",
            "Triggers the previous item in the active media playlist.", "GET", "v1/trigger/media/previous");
        Add("misc.find_my_mouse", "General", "Find my mouse",
            "Makes the ProPresenter machine reveal its cursor. Handy for testing that a trigger fired.",
            "GET", "v1/find_my_mouse");

        // --------------------------------------------------------- Playlist
        // These are the "advance to the next presentation, not the next slide" actions.
        Add("playlist.focused.next.trigger", "Playlist", "Next presentation in focused playlist",
            "Triggers the next item of the focused playlist — advances to the next presentation, not the next slide.",
            "GET", "v1/playlist/focused/next/trigger");
        Add("playlist.focused.previous.trigger", "Playlist", "Previous presentation in focused playlist",
            "Triggers the previous item of the focused playlist.",
            "GET", "v1/playlist/focused/previous/trigger");
        Add("playlist.focused.trigger", "Playlist", "Retrigger focused playlist",
            "Retriggers the focused playlist from its first item.", "GET", "v1/playlist/focused/trigger");
        Add("playlist.focused.item.trigger", "Playlist", "Trigger item in focused playlist",
            "Triggers a specific item of the focused playlist by index.",
            "GET", "v1/playlist/focused/{index}/trigger", [Index()]);
        Add("playlist.focused.item.cue.trigger", "Playlist", "Trigger cue of item in focused playlist",
            "Triggers a specific cue within a specific item of the focused playlist.",
            "GET", "v1/playlist/focused/{index}/{cue_index}/trigger",
            [Index(), Index("cue_index", "Cue index")]);
        Add("playlist.focused.item.timeline", "Playlist", "Focused playlist item timeline",
            "Performs a timeline operation on an item of the focused playlist.",
            "GET", "v1/playlist/focused/{index}/timeline/{operation}",
            [Index(), new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);

        Add("playlist.next.focus", "Playlist", "Focus next playlist",
            "Moves playlist focus to the next playlist.", "GET", "v1/playlist/next/focus");
        Add("playlist.previous.focus", "Playlist", "Focus previous playlist",
            "Moves playlist focus to the previous playlist.", "GET", "v1/playlist/previous/focus");

        Add("playlist.byid.focus", "Playlist", "Focus a playlist",
            "Sets playlist focus to a specific playlist.", "GET", "v1/playlist/{playlist_id}/focus",
            [new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId)]);
        Add("playlist.byid.trigger", "Playlist", "Trigger a playlist",
            "Triggers a specific playlist from its first item.", "GET", "v1/playlist/{playlist_id}/trigger",
            [new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId)]);
        Add("playlist.byid.next.trigger", "Playlist", "Next item in a playlist",
            "Triggers the next item of a specific playlist.", "GET", "v1/playlist/{playlist_id}/next/trigger",
            [new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId)]);
        Add("playlist.byid.previous.trigger", "Playlist", "Previous item in a playlist",
            "Triggers the previous item of a specific playlist.", "GET", "v1/playlist/{playlist_id}/previous/trigger",
            [new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId)]);
        Add("playlist.byid.item.trigger", "Playlist", "Trigger item in a playlist",
            "Triggers a specific item of a specific playlist by index.",
            "GET", "v1/playlist/{playlist_id}/{index}/trigger",
            [new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId), Index()]);
        Add("playlist.byid.item.cue.trigger", "Playlist", "Trigger cue of item in a playlist",
            "Triggers a specific cue within a specific item of a specific playlist.",
            "GET", "v1/playlist/{playlist_id}/{index}/{cue_index}/trigger",
            [
                new ActionParameter("playlist_id", "Playlist", ParameterKind.PresentationPlaylistId),
                Index(), Index("cue_index", "Cue index"),
            ]);

        Add("playlist.active.presentation.trigger", "Playlist", "Retrigger active presentation playlist",
            "Retriggers the active presentation playlist.", "GET", "v1/playlist/active/presentation/trigger");
        Add("playlist.active.presentation.focus", "Playlist", "Focus active presentation playlist",
            "Focuses the active presentation playlist.", "GET", "v1/playlist/active/presentation/focus");
        Add("playlist.active.presentation.item.trigger", "Playlist", "Trigger item in active presentation playlist",
            "Triggers a specific item of the active presentation playlist.",
            "GET", "v1/playlist/active/presentation/{index}/trigger", [Index()]);
        Add("playlist.active.presentation.item.cue.trigger", "Playlist", "Trigger cue in active presentation playlist",
            "Triggers a specific cue of a specific item in the active presentation playlist.",
            "GET", "v1/playlist/active/presentation/{index}/{cue_index}/trigger",
            [Index(), Index("cue_index", "Cue index")]);
        Add("playlist.active.presentation.item.timeline", "Playlist", "Active presentation playlist item timeline",
            "Performs a timeline operation on an item of the active presentation playlist.",
            "GET", "v1/playlist/active/presentation/{index}/timeline/{operation}",
            [Index(), new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);

        Add("playlist.active.announcement.trigger", "Playlist", "Retrigger active announcement playlist",
            "Retriggers the active announcement playlist.", "GET", "v1/playlist/active/announcement/trigger");
        Add("playlist.active.announcement.focus", "Playlist", "Focus active announcement playlist",
            "Focuses the active announcement playlist.", "GET", "v1/playlist/active/announcement/focus");
        Add("playlist.active.announcement.item.trigger", "Playlist", "Trigger item in active announcement playlist",
            "Triggers a specific item of the active announcement playlist.",
            "GET", "v1/playlist/active/announcement/{index}/trigger", [Index()]);
        Add("playlist.active.announcement.item.cue.trigger", "Playlist", "Trigger cue in active announcement playlist",
            "Triggers a specific cue of a specific item in the active announcement playlist.",
            "GET", "v1/playlist/active/announcement/{index}/{cue_index}/trigger",
            [Index(), Index("cue_index", "Cue index")]);
        Add("playlist.active.announcement.item.timeline", "Playlist", "Active announcement playlist item timeline",
            "Performs a timeline operation on an item of the active announcement playlist.",
            "GET", "v1/playlist/active/announcement/{index}/timeline/{operation}",
            [Index(), new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);
        Add("playlist.byid.item.timeline", "Playlist", "Playlist item timeline",
            "Performs a timeline operation on an item of a specific playlist.",
            "GET", "v1/playlist/{id}/{index}/timeline/{operation}",
            [
                new ActionParameter("id", "Playlist", ParameterKind.PresentationPlaylistId),
                Index(),
                new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations),
            ]);

        // ----------------------------------------------------- Presentation
        Add("presentation.active.next", "Presentation", "Next slide (active presentation)",
            "Advances the active presentation by one cue.", "GET", "v1/presentation/active/next/trigger");
        Add("presentation.active.previous", "Presentation", "Previous slide (active presentation)",
            "Steps the active presentation back one cue.", "GET", "v1/presentation/active/previous/trigger");
        Add("presentation.active.retrigger", "Presentation", "Restart active presentation",
            "Retriggers the active presentation from its beginning.", "GET", "v1/presentation/active/trigger");
        Add("presentation.active.slide", "Presentation", "Go to slide (active presentation)",
            "Triggers a specific cue of the active presentation.",
            "GET", "v1/presentation/active/{index}/trigger", [Index("index", "Slide index")]);
        Add("presentation.active.group", "Presentation", "Trigger group (active presentation)",
            "Triggers a named group (e.g. Chorus) in the active presentation.",
            "GET", "v1/presentation/active/group/{group_id}/trigger",
            [new ActionParameter("group_id", "Group", ParameterKind.GroupId)]);
        Add("presentation.active.timeline", "Presentation", "Active presentation timeline",
            "Plays, pauses or rewinds the active presentation's timeline.",
            "GET", "v1/presentation/active/timeline/{operation}",
            [new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);
        Add("presentation.active.focus", "Presentation", "Focus active presentation",
            "Moves focus to the active presentation.", "GET", "v1/presentation/active/focus");

        Add("presentation.focused.next", "Presentation", "Next slide (focused presentation)",
            "Advances the focused presentation by one cue.", "GET", "v1/presentation/focused/next/trigger");
        Add("presentation.focused.previous", "Presentation", "Previous slide (focused presentation)",
            "Steps the focused presentation back one cue.", "GET", "v1/presentation/focused/previous/trigger");
        Add("presentation.focused.retrigger", "Presentation", "Restart focused presentation",
            "Retriggers the focused presentation from its beginning.", "GET", "v1/presentation/focused/trigger");
        Add("presentation.focused.slide", "Presentation", "Go to slide (focused presentation)",
            "Triggers a specific cue of the focused presentation.",
            "GET", "v1/presentation/focused/{index}/trigger", [Index("index", "Slide index")]);
        Add("presentation.focused.group", "Presentation", "Trigger group (focused presentation)",
            "Triggers a named group in the focused presentation.",
            "GET", "v1/presentation/focused/group/{group_id}/trigger",
            [new ActionParameter("group_id", "Group", ParameterKind.GroupId)]);
        Add("presentation.focused.timeline", "Presentation", "Focused presentation timeline",
            "Plays, pauses or rewinds the focused presentation's timeline.",
            "GET", "v1/presentation/focused/timeline/{operation}",
            [new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);

        Add("presentation.next.focus", "Presentation", "Focus next presentation",
            "Moves presentation focus forward one item in the playlist.", "GET", "v1/presentation/next/focus");
        Add("presentation.previous.focus", "Presentation", "Focus previous presentation",
            "Moves presentation focus back one item in the playlist.", "GET", "v1/presentation/previous/focus");

        Add("presentation.byuuid.trigger", "Presentation", "Trigger presentation by UUID",
            "Triggers a specific presentation from its beginning.", "GET", "v1/presentation/{uuid}/trigger",
            [new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text, "Copy from the presentation's API details.")]);
        Add("presentation.byuuid.slide", "Presentation", "Go to slide of presentation by UUID",
            "Triggers a specific cue of a specific presentation.", "GET", "v1/presentation/{uuid}/{index}/trigger",
            [new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text), Index("index", "Slide index")]);
        Add("presentation.byuuid.next", "Presentation", "Next slide of presentation by UUID",
            "Advances a specific presentation by one cue.", "GET", "v1/presentation/{uuid}/next/trigger",
            [new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text)]);
        Add("presentation.byuuid.previous", "Presentation", "Previous slide of presentation by UUID",
            "Steps a specific presentation back one cue.", "GET", "v1/presentation/{uuid}/previous/trigger",
            [new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text)]);
        Add("presentation.byuuid.group", "Presentation", "Trigger group of presentation by UUID",
            "Triggers a named group in a specific presentation.",
            "GET", "v1/presentation/{uuid}/group/{group_id}/trigger",
            [
                new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text),
                new ActionParameter("group_id", "Group", ParameterKind.GroupId),
            ]);
        Add("presentation.byuuid.focus", "Presentation", "Focus presentation by UUID",
            "Moves focus to a specific presentation.", "GET", "v1/presentation/{uuid}/focus",
            [new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text)]);
        Add("presentation.byuuid.timeline", "Presentation", "Timeline of presentation by UUID",
            "Plays, pauses or rewinds a specific presentation's timeline.",
            "GET", "v1/presentation/{uuid}/timeline/{operation}",
            [
                new ActionParameter("uuid", "Presentation UUID", ParameterKind.Text),
                new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations),
            ]);

        Add("library.presentation.trigger", "Presentation", "Trigger library presentation",
            "Triggers a presentation from a library.", "GET", "v1/library/{library_id}/{presentation_id}/trigger",
            [
                new ActionParameter("library_id", "Library", ParameterKind.LibraryId),
                new ActionParameter("presentation_id", "Presentation", ParameterKind.Text, "Presentation name or UUID within the library."),
            ]);
        Add("library.presentation.slide.trigger", "Presentation", "Trigger slide of library presentation",
            "Triggers a specific cue of a library presentation.",
            "GET", "v1/library/{library_id}/{presentation_id}/{index}/trigger",
            [
                new ActionParameter("library_id", "Library", ParameterKind.LibraryId),
                new ActionParameter("presentation_id", "Presentation", ParameterKind.Text),
                Index("index", "Slide index"),
            ]);

        // ---------------------------------------------------- Announcement
        Add("announcement.active.trigger", "Announcement", "Restart active announcement",
            "Retriggers the active announcement presentation.", "GET", "v1/announcement/active/trigger");
        Add("announcement.active.next", "Announcement", "Next announcement cue",
            "Advances the active announcement by one cue.", "GET", "v1/announcement/active/next/trigger");
        Add("announcement.active.previous", "Announcement", "Previous announcement cue",
            "Steps the active announcement back one cue.", "GET", "v1/announcement/active/previous/trigger");
        Add("announcement.active.slide", "Announcement", "Go to announcement cue",
            "Triggers a specific cue of the active announcement.",
            "GET", "v1/announcement/active/{index}/trigger", [Index("index", "Cue index")]);
        Add("announcement.active.focus", "Announcement", "Focus active announcement",
            "Moves focus to the active announcement.", "GET", "v1/announcement/active/focus");
        Add("announcement.active.timeline", "Announcement", "Announcement timeline",
            "Plays, pauses or rewinds the active announcement's timeline.",
            "GET", "v1/announcement/active/timeline/{operation}",
            [new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "play", TimelineOperations)]);

        // ------------------------------------------------------------ Audio
        Add("audio.active.next", "Audio", "Next item in active audio playlist",
            "Triggers the next item in the active audio playlist.", "GET", "v1/audio/playlist/active/next/trigger");
        Add("audio.active.previous", "Audio", "Previous item in active audio playlist",
            "Triggers the previous item in the active audio playlist.", "GET", "v1/audio/playlist/active/previous/trigger");
        Add("audio.active.trigger", "Audio", "Restart active audio playlist",
            "Restarts the active audio playlist from its first item.", "GET", "v1/audio/playlist/active/trigger");
        Add("audio.active.item", "Audio", "Trigger item in active audio playlist",
            "Triggers a specific item of the active audio playlist.",
            "GET", "v1/audio/playlist/active/{id}/trigger",
            [new ActionParameter("id", "Item", ParameterKind.Text, "Item name, index or UUID.")]);
        Add("audio.focused.next", "Audio", "Next item in focused audio playlist",
            "Triggers the next item in the focused audio playlist.", "GET", "v1/audio/playlist/focused/next/trigger");
        Add("audio.focused.previous", "Audio", "Previous item in focused audio playlist",
            "Triggers the previous item in the focused audio playlist.", "GET", "v1/audio/playlist/focused/previous/trigger");
        Add("audio.focused.trigger", "Audio", "Restart focused audio playlist",
            "Restarts the focused audio playlist.", "GET", "v1/audio/playlist/focused/trigger");
        Add("audio.byid.trigger", "Audio", "Start an audio playlist",
            "Starts a specific audio playlist from its first item — e.g. a backing-track or countdown-music playlist.",
            "GET", "v1/audio/playlist/{playlist_id}/trigger",
            [new ActionParameter("playlist_id", "Audio playlist", ParameterKind.AudioPlaylistId)]);
        Add("audio.byid.focus", "Audio", "Focus an audio playlist",
            "Focuses a specific audio playlist.", "GET", "v1/audio/playlist/{playlist_id}/focus",
            [new ActionParameter("playlist_id", "Audio playlist", ParameterKind.AudioPlaylistId)]);
        Add("audio.byid.next", "Audio", "Next item in an audio playlist",
            "Triggers the next item of a specific audio playlist.", "GET", "v1/audio/playlist/{playlist_id}/next/trigger",
            [new ActionParameter("playlist_id", "Audio playlist", ParameterKind.AudioPlaylistId)]);
        Add("audio.byid.previous", "Audio", "Previous item in an audio playlist",
            "Triggers the previous item of a specific audio playlist.",
            "GET", "v1/audio/playlist/{playlist_id}/previous/trigger",
            [new ActionParameter("playlist_id", "Audio playlist", ParameterKind.AudioPlaylistId)]);
        Add("audio.byid.item", "Audio", "Trigger item in an audio playlist",
            "Triggers a specific item of a specific audio playlist.",
            "GET", "v1/audio/playlist/{playlist_id}/{id}/trigger",
            [
                new ActionParameter("playlist_id", "Audio playlist", ParameterKind.AudioPlaylistId),
                new ActionParameter("id", "Item", ParameterKind.Text, "Item name, index or UUID."),
            ]);
        Add("audio.focused.item", "Audio", "Trigger item in focused audio playlist",
            "Triggers a specific item of the focused audio playlist.",
            "GET", "v1/audio/playlist/focused/{id}/trigger",
            [new ActionParameter("id", "Item", ParameterKind.Text, "Item name, index or UUID.")]);
        Add("audio.active.focus", "Audio", "Focus active audio playlist",
            "Moves audio playlist focus to the active audio playlist.", "GET", "v1/audio/playlist/active/focus");
        Add("audio.next.focus", "Audio", "Focus next audio playlist",
            "Moves audio playlist focus forward.", "GET", "v1/audio/playlist/next/focus");
        Add("audio.previous.focus", "Audio", "Focus previous audio playlist",
            "Moves audio playlist focus back.", "GET", "v1/audio/playlist/previous/focus");

        // ------------------------------------------------------------ Media
        Add("media.active.next", "Media", "Next item in active media playlist",
            "Triggers the next item in the active media playlist.", "GET", "v1/media/playlist/active/next/trigger");
        Add("media.active.previous", "Media", "Previous item in active media playlist",
            "Triggers the previous item in the active media playlist.", "GET", "v1/media/playlist/active/previous/trigger");
        Add("media.active.trigger", "Media", "Restart active media playlist",
            "Restarts the active media playlist.", "GET", "v1/media/playlist/active/trigger");
        Add("media.active.item", "Media", "Trigger item in active media playlist",
            "Triggers a specific item of the active media playlist.",
            "GET", "v1/media/playlist/active/{media_id}/trigger",
            [new ActionParameter("media_id", "Media item", ParameterKind.Text, "Item name, index or UUID.")]);
        Add("media.focused.next", "Media", "Next item in focused media playlist",
            "Triggers the next item in the focused media playlist.", "GET", "v1/media/playlist/focused/next/trigger");
        Add("media.focused.previous", "Media", "Previous item in focused media playlist",
            "Triggers the previous item in the focused media playlist.", "GET", "v1/media/playlist/focused/previous/trigger");
        Add("media.focused.trigger", "Media", "Restart focused media playlist",
            "Restarts the focused media playlist.", "GET", "v1/media/playlist/focused/trigger");
        Add("media.byid.trigger", "Media", "Start a media playlist",
            "Starts a specific media playlist from its first item.", "GET", "v1/media/playlist/{playlist_id}/trigger",
            [new ActionParameter("playlist_id", "Media playlist", ParameterKind.MediaPlaylistId)]);
        Add("media.byid.focus", "Media", "Focus a media playlist",
            "Focuses a specific media playlist.", "GET", "v1/media/playlist/{playlist_id}/focus",
            [new ActionParameter("playlist_id", "Media playlist", ParameterKind.MediaPlaylistId)]);
        Add("media.byid.next", "Media", "Next item in a media playlist",
            "Triggers the next item of a specific media playlist.", "GET", "v1/media/playlist/{playlist_id}/next/trigger",
            [new ActionParameter("playlist_id", "Media playlist", ParameterKind.MediaPlaylistId)]);
        Add("media.byid.previous", "Media", "Previous item in a media playlist",
            "Triggers the previous item of a specific media playlist.",
            "GET", "v1/media/playlist/{playlist_id}/previous/trigger",
            [new ActionParameter("playlist_id", "Media playlist", ParameterKind.MediaPlaylistId)]);
        Add("media.byid.item", "Media", "Trigger item in a media playlist",
            "Triggers a specific item of a specific media playlist.",
            "GET", "v1/media/playlist/{playlist_id}/{media_id}/trigger",
            [
                new ActionParameter("playlist_id", "Media playlist", ParameterKind.MediaPlaylistId),
                new ActionParameter("media_id", "Media item", ParameterKind.Text, "Item name, index or UUID."),
            ]);
        Add("media.focused.item", "Media", "Trigger item in focused media playlist",
            "Triggers a specific item of the focused media playlist.",
            "GET", "v1/media/playlist/focused/{media_id}/trigger",
            [new ActionParameter("media_id", "Media item", ParameterKind.Text, "Item name, index or UUID.")]);
        Add("media.active.focus", "Media", "Focus active media playlist",
            "Moves media playlist focus to the active media playlist.", "GET", "v1/media/playlist/active/focus");
        Add("media.next.focus", "Media", "Focus next media playlist",
            "Moves media playlist focus forward.", "GET", "v1/media/playlist/next/focus");
        Add("media.previous.focus", "Media", "Focus previous media playlist",
            "Moves media playlist focus back.", "GET", "v1/media/playlist/previous/focus");

        // ------------------------------------------------------------ Clear
        Add("clear.group.trigger", "Clear", "Trigger clear group",
            "Runs one of ProPresenter's configured clear groups.", "GET", "v1/clear/group/{id}/trigger",
            [new ActionParameter("id", "Clear group", ParameterKind.ClearGroupId)]);
        Add("clear.layer", "Clear", "Clear a layer",
            "Clears a single layer.", "GET", "v1/clear/layer/{layer}",
            [new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "slide", ClearableLayers)]);

        // ------------------------------------------------------------- Look
        Add("look.trigger", "Look", "Trigger look",
            "Switches the audience output to a saved look.", "GET", "v1/look/{id}/trigger",
            [new ActionParameter("id", "Look", ParameterKind.LookId)]);

        // ------------------------------------------------------------- Prop
        Add("prop.trigger", "Prop", "Show prop",
            "Shows a prop.", "GET", "v1/prop/{id}/trigger",
            [new ActionParameter("id", "Prop", ParameterKind.PropId)]);
        Add("prop.clear", "Prop", "Clear prop",
            "Clears a prop.", "GET", "v1/prop/{id}/clear",
            [new ActionParameter("id", "Prop", ParameterKind.PropId)]);
        Add("prop.auto_clear.pause", "Prop", "Pause prop auto-clear",
            "Pauses the auto-clear countdown for a prop.", "GET", "v1/prop/{id}/auto_clear/pause",
            [new ActionParameter("id", "Prop", ParameterKind.PropId)]);
        Add("prop.auto_clear.resume", "Prop", "Resume prop auto-clear",
            "Resumes the auto-clear countdown for a prop.", "GET", "v1/prop/{id}/auto_clear/resume",
            [new ActionParameter("id", "Prop", ParameterKind.PropId)]);

        // ---------------------------------------------------------- Message
        Add("message.trigger", "Message", "Show message",
            "Shows a message. Token values are supplied as a JSON array matching the API's message trigger schema.",
            "POST", "v1/message/{id}/trigger",
            [
                new ActionParameter("id", "Message", ParameterKind.MessageId),
                new ActionParameter("tokens", "Token values (JSON)", ParameterKind.Json,
                    "JSON array of token objects, e.g. [{\"name\":\"Text\",\"text\":{\"text\":\"Hello\"}}]. Use [] for none.",
                    "[]"),
            ],
            BodySource.JsonParameter, "tokens");
        Add("message.clear", "Message", "Clear message",
            "Clears a message.", "GET", "v1/message/{id}/clear",
            [new ActionParameter("id", "Message", ParameterKind.MessageId)]);

        // ------------------------------------------------------------ Stage
        Add("stage.screen.layout", "Stage", "Set stage screen layout",
            "Assigns a stage layout to a stage screen.", "GET", "v1/stage/screen/{id}/layout/{layout_id}",
            [
                new ActionParameter("id", "Stage screen", ParameterKind.StageScreenId),
                new ActionParameter("layout_id", "Stage layout", ParameterKind.StageLayoutId),
            ]);
        Add("stage.message.show", "Stage", "Show stage message",
            "Shows a message on the stage screens.", "PUT", "v1/stage/message",
            [new ActionParameter("message", "Message text", ParameterKind.Text, null, "")],
            BodySource.StringParameter, "message");
        Add("stage.message.clear", "Stage", "Clear stage message",
            "Hides the current stage message.", "DELETE", "v1/stage/message");

        // ------------------------------------------------------------ Timer
        // Timers can drive other timers: a chained countdown is a common service pattern.
        Add("timer.operation", "Timer", "Start / stop / reset a timer",
            "Performs an operation on another ProPresenter timer.", "GET", "v1/timer/{id}/{operation}",
            [
                new ActionParameter("id", "Timer", ParameterKind.TimerId),
                new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "start", TimerOperations),
            ]);
        Add("timer.all.operation", "Timer", "Start / stop / reset all timers",
            "Performs an operation on every configured timer.", "GET", "v1/timers/{operation}",
            [new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "stop", TimerOperations)]);
        Add("timer.increment", "Timer", "Add time to a running timer",
            "Adds seconds to a running timer. Use a negative value to remove time.",
            "GET", "v1/timer/{id}/increment/{time}",
            [
                new ActionParameter("id", "Timer", ParameterKind.TimerId),
                new ActionParameter("time", "Seconds", ParameterKind.Seconds, "Negative values subtract time.", "60"),
            ]);

        // -------------------------------------------------------- Transport
        Add("transport.play", "Transport", "Play layer",
            "Resumes playback on a layer.", "GET", "v1/transport/{layer}/play",
            [new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers)]);
        Add("transport.pause", "Transport", "Pause layer",
            "Pauses playback on a layer.", "GET", "v1/transport/{layer}/pause",
            [new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers)]);
        Add("transport.go_to_end", "Transport", "Skip to end of layer",
            "Jumps to the end of the media playing on a layer.", "GET", "v1/transport/{layer}/go_to_end",
            [new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers)]);
        Add("transport.skip_forward", "Transport", "Skip forward",
            "Skips forward on a layer by a number of seconds.", "GET", "v1/transport/{layer}/skip_forward/{time}",
            [
                new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers),
                new ActionParameter("time", "Seconds", ParameterKind.Seconds, null, "15"),
            ]);
        Add("transport.skip_backward", "Transport", "Skip backward",
            "Skips backward on a layer by a number of seconds.", "GET", "v1/transport/{layer}/skip_backward/{time}",
            [
                new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers),
                new ActionParameter("time", "Seconds", ParameterKind.Seconds, null, "15"),
            ]);
        Add("transport.seek", "Transport", "Seek layer to a time",
            "Jumps the media on a layer to a specific time.", "PUT", "v1/transport/{layer}/time",
            [
                new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers),
                new ActionParameter("seconds", "Seconds", ParameterKind.Seconds, "Position in seconds from the start.", "0"),
            ],
            BodySource.NumberParameter, "seconds");
        Add("transport.auto_advance.disable", "Transport", "Cancel auto-advance",
            "Cancels the pending auto-advance on a layer.", "DELETE", "v1/transport/{layer}/auto_advance",
            [new ActionParameter("layer", "Layer", ParameterKind.Choice, null, "presentation", TransportLayers)]);

        // ----------------------------------------------------- Video inputs
        Add("video_input.trigger", "Video input", "Trigger video input",
            "Sends a video input to the output.", "GET", "v1/video_inputs/{id}/trigger",
            [new ActionParameter("id", "Video input", ParameterKind.VideoInputId)]);

        // ---------------------------------------------------------- Capture
        Add("capture.operation", "Capture", "Start / stop capture",
            "Starts or stops recording/streaming.", "GET", "v1/capture/{operation}",
            [new ActionParameter("operation", "Operation", ParameterKind.Choice, null, "start", CaptureOperations)]);

        // ----------------------------------------------------------- Screens
        Add("status.audience_screens", "Screens", "Enable / disable audience screens",
            "Turns the audience screens on or off.", "PUT", "v1/status/audience_screens",
            [new ActionParameter("enabled", "Enabled", ParameterKind.Boolean, null, "true")],
            BodySource.BooleanParameter, "enabled");
        Add("status.stage_screens", "Screens", "Enable / disable stage screens",
            "Turns the stage screens on or off.", "PUT", "v1/status/stage_screens",
            [new ActionParameter("enabled", "Enabled", ParameterKind.Boolean, null, "true")],
            BodySource.BooleanParameter, "enabled");

        // ------------------------------------------------------ FOH lights
        // Not ProPresenter calls: these switch the Shelly relay configured under Settings → FOH lights.
        var channels = new ActionParameter("channels", "Channels", ParameterKind.Text,
            "Relay outputs to switch: \"all\", or a list such as \"0,1,2,3\".", "all");
        var order = new ActionParameter("order", "Order", ParameterKind.Choice,
            "\"in order\" follows Settings → FOH lights → Order (on first to last, off last to first); "
            + "\"all together\" switches every output at once.", "in order", LightOrders);

        a.Add(new ActionDefinition("lights.set", "FOH lights", "Turn lights on / off",
            "Switches the FOH lights. Also cancels a pending \"lights off for this video\".", "GET", "",
            [
                new ActionParameter("state", "State", ParameterKind.Choice, null, "off", LightStates),
                channels,
                new ActionParameter("failsafe", "Switch back after (s)", ParameterKind.Seconds,
                    "The relay itself flips back after this many seconds, even if this app is not running. 0 = never.", "0"),
                order,
            ],
            Target: ActionTarget.Lights));

        a.Add(new ActionDefinition("lights.off_for_video", "FOH lights", "Lights off for this video",
            "Turns the FOH lights off, then back on when the video playing on the presentation layer ends. "
            + "Put this on a macro attached to the video's cue.", "GET", "",
            [
                channels,
                new ActionParameter("failsafe", "Failsafe margin (s)", ParameterKind.Seconds,
                    "Once the video's length is known the relay is told to come back on by itself this long after "
                    + "the video should finish, in case this app or ProPresenter goes away. 0 = no failsafe. Off by default "
                    + "until the relay's flip-back behaviour is confirmed — see docs/LIGHTS.md.", "0"),
                order,
            ],
            Target: ActionTarget.Lights));

        return a;
    }
}
