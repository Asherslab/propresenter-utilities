using ProPresenter.Api.Shelly;
using ProPresenter.Triggers.Lights;

namespace ProPresenter.Triggers.Tests;

public class LightsTests
{
    [Fact]
    public void Switch_set_uses_the_gen2_rpc_form()
    {
        Assert.Equal(
            "http://192.168.1.50/rpc/Switch.Set?id=2&on=false",
            ShellyClient.BuildSetUri("192.168.1.50", 2, on: false, toggleAfter: null).ToString());
    }

    [Fact]
    public void Failsafe_becomes_toggle_after_in_whole_seconds()
    {
        Assert.Equal(
            "http://192.168.1.50/rpc/Switch.Set?id=0&on=false&toggle_after=46",
            ShellyClient.BuildSetUri("192.168.1.50", 0, on: false, TimeSpan.FromSeconds(45.2)).ToString());
    }

    [Theory]
    [InlineData("192.168.1.50", "http://192.168.1.50/")]
    [InlineData(" 192.168.1.50/ ", "http://192.168.1.50/")]
    [InlineData("http://shelly-foh.local", "http://shelly-foh.local/")]
    [InlineData("10.0.0.9:8080", "http://10.0.0.9:8080/")]
    public void Accepts_hosts_with_or_without_a_scheme(string host, string expected)
    {
        Assert.Equal(expected, ShellyClient.BaseUri(host).ToString());
    }

    [Fact]
    public void Several_outputs_go_in_one_SetMany_request()
    {
        Assert.Equal(
            """{"id":1,"method":"Switch.SetMany","params":{"ids":[0,1,2,3],"on":false,"toggle_after":46}}""",
            ShellyClient.BuildSetManyRequest([0, 1, 2, 3], on: false, TimeSpan.FromSeconds(45.2)));

        Assert.Equal(
            """{"id":1,"method":"Switch.SetMany","params":{"ids":[0,1],"on":true}}""",
            ShellyClient.BuildSetManyRequest([0, 1], on: true, toggleAfter: null));
    }

    [Fact]
    public void SetMany_results_map_to_outputs_in_order()
    {
        var results = ShellyClient.ParseSetManyResponse([0, 1, 2], 200,
            """{"id":1,"src":"shellypro4pm","result":{"results":[null,{"code":-105,"message":"id 1 not found"},null]}}""");

        Assert.NotNull(results);
        Assert.Equal([true, false, true], results!.Select(r => r.Success));
        Assert.Equal("id 1 not found", results[1].Error);
    }

    [Theory]
    [InlineData(404, """{"id":1,"src":"shellypro4pm","error":{"code":404,"message":"No handler for Switch.SetMany"}}""")]
    [InlineData(200, """{"id":1,"src":"shellypro4pm","error":{"code":-114,"message":"Method Switch.SetMany failed"}}""")]
    [InlineData(501, "<html>Not implemented</html>")]
    public void Firmware_without_SetMany_falls_back(int status, string body)
    {
        Assert.Null(ShellyClient.ParseSetManyResponse([0, 1, 2, 3], status, body));
    }

    [Fact]
    public void The_embedded_script_carries_the_version_marker_it_is_recognised_by()
    {
        Assert.StartsWith(ShellyClient.ScriptMarker, ShellyClient.ScriptCode);
        Assert.Contains("HTTPServer.registerEndpoint(\"set\"", ShellyClient.ScriptCode);
    }

    [Fact]
    public void Script_upload_chunks_reassemble_to_the_script()
    {
        var chunks = ShellyClient.Chunks(ShellyClient.ScriptCode).ToList();

        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 1000));
        Assert.Equal(ShellyClient.ScriptCode, string.Concat(chunks));
    }

    [Fact]
    public void Script_request_names_every_output()
    {
        Assert.Equal(
            "http://192.168.1.50/script/1/set?ids=0,1,2,3&on=false&toggle_after=46",
            ShellyClient.BuildScriptUri("192.168.1.50", 1, [0, 1, 2, 3], on: false, TimeSpan.FromSeconds(45.2)).ToString());
    }

    [Fact]
    public void Script_results_map_to_outputs_in_order()
    {
        var results = ShellyClient.ParseScriptResponse([0, 7], """{"results":[null,"Argument 'id', value 7 not found!"]}""");

        Assert.NotNull(results);
        Assert.True(results![0].Success);
        Assert.False(results[1].Success);
        Assert.Equal("Argument 'id', value 7 not found!", results[1].Error);
        Assert.Null(ShellyClient.ParseScriptResponse([0], "ids required"));
    }

    [Fact]
    public void All_means_the_configured_outputs()
    {
        Assert.Equal([0, 1, 2, 3], LightsController.ParseChannels("all", "0,1,2,3"));
        Assert.Equal([0, 1, 2, 3], LightsController.ParseChannels("", "0,1,2,3"));
        Assert.Equal([1, 3], LightsController.ParseChannels("all", "1, 3"));
    }

    [Fact]
    public void An_explicit_list_overrides_the_configured_outputs()
    {
        Assert.Equal([0, 2], LightsController.ParseChannels("0,2,2", "0,1,2,3"));
    }

    [Theory]
    [InlineData("left")]
    [InlineData("-1")]
    public void Rejects_outputs_that_are_not_numbers(string spec)
    {
        Assert.Throws<FormatException>(() => LightsController.ParseChannels(spec, "0,1,2,3"));
    }
}
