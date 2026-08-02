using System.Text.Json;
using ProPresenter.Api;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Round-trips payloads captured from a live ProPresenter 21.4 instance. Deserialization failures here
/// are invisible until runtime, so the exact wire shapes are pinned.
/// </summary>
public class ModelDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private const string CurrentPayload = """
        [{"id":{"uuid":"C56CCF4E-332C-4E77-9E00-C158470795BD","name":"Segment Countdown","index":0},"time":"00:05:00","state":"stopped"},
         {"id":{"uuid":"AB0E7EE9-B54B-4087-A855-34B9142EB7DF","name":"PreShow Countdown","index":1},"time":"05:00:00","state":"running"},
         {"id":{"uuid":"1C19D5BF-305C-4DB3-9006-773563CE8218","name":"Game Timer","index":2},"time":"-00:00:02","state":"overrunning"}]
        """;

    private const string TimersPayload = """
        [{"id":{"name":"Segment Countdown","index":0,"uuid":"C56CCF4E-332C-4E77-9E00-C158470795BD"},"allows_overrun":false,"countdown":{"duration":300}},
         {"id":{"name":"PreShow Countdown","index":1,"uuid":"AB0E7EE9-B54B-4087-A855-34B9142EB7DF"},"allows_overrun":false,"count_down_to_time":{"time_of_day":18000,"period":"pm"}},
         {"id":{"name":"Game Timer","index":2,"uuid":"1C19D5BF-305C-4DB3-9006-773563CE8218"},"allows_overrun":true,"elapsed":{"start_time":0,"end_time":120}}]
        """;

    [Fact]
    public void Current_timers_payload_deserializes()
    {
        var timers = JsonSerializer.Deserialize<List<TimerCurrent>>(CurrentPayload, Options);

        Assert.NotNull(timers);
        Assert.Equal(3, timers.Count);

        Assert.Equal("Segment Countdown", timers[0].Id.Name);
        Assert.Equal(TimerState.Stopped, timers[0].State);
        Assert.Equal(TimeSpan.FromMinutes(5), timers[0].Value);

        Assert.Equal(TimerState.Running, timers[1].State);
        Assert.Equal(TimeSpan.FromHours(5), timers[1].Value);

        Assert.Equal(TimerState.Overrunning, timers[2].State);
        Assert.Equal(TimeSpan.FromSeconds(-2), timers[2].Value);
    }

    [Fact]
    public void Current_timers_payload_also_serializes()
    {
        // A computed property colliding with a wire property throws only on serialize, which is how
        // this surfaced in the first place: the stream client failed on reconnect logging.
        var timers = JsonSerializer.Deserialize<List<TimerCurrent>>(CurrentPayload, Options)!;

        var json = JsonSerializer.Serialize(timers, Options);

        Assert.Contains("\"state\":\"stopped\"", json);
    }

    [Fact]
    public void Timer_definitions_payload_deserializes_all_three_kinds()
    {
        var timers = JsonSerializer.Deserialize<List<TimerConfiguration>>(TimersPayload, Options);

        Assert.NotNull(timers);

        Assert.Equal(TimerKind.Countdown, timers[0].Kind);
        Assert.Equal(TimeSpan.FromMinutes(5), timers[0].TotalDuration);
        Assert.False(timers[0].AllowsOverrun);

        Assert.Equal(TimerKind.CountDownToTime, timers[1].Kind);
        Assert.Equal("pm", timers[1].CountDownToTime!.Period);
        Assert.Null(timers[1].TotalDuration); // depends on the wall clock when it starts

        Assert.Equal(TimerKind.Elapsed, timers[2].Kind);
        Assert.Equal(TimeSpan.FromSeconds(120), timers[2].TotalDuration);
        Assert.True(timers[2].AllowsOverrun);
    }

    [Fact]
    public void Version_payload_deserializes()
    {
        const string payload = """
            {"name":"JRGVJKXVGF-MAC","platform":"mac","os_version":"15.7.4","host_description":"ProPresenter 21.4","api_version":"v1"}
            """;

        var version = JsonSerializer.Deserialize<VersionInfo>(payload, Options);

        Assert.Equal("ProPresenter 21.4", version!.HostDescription);
        Assert.Equal("v1", version.ApiVersion);
    }
}
