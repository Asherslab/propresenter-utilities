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
