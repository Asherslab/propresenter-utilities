using ProPresenter.Api;
using ProPresenter.Api.Actions;
using ProPresenter.Api.Shelly;
using ProPresenter.App.Components;
using ProPresenter.App.Services;
using ProPresenter.Triggers;
using ProPresenter.Triggers.Lights;
using ProPresenter.Triggers.Signals;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Follow the executable rather than the working directory: launchd, systemd and the Windows
    // service manager all start processes from somewhere unhelpful.
    ContentRootPath = AppContext.BaseDirectory,
});

// CreateBuilder only wires these up automatically in Development, and this app normally runs in
// Production under a service manager. Without it the UI is served unstyled.
builder.WebHost.UseStaticWebAssets();

builder.Configuration.AddEnvironmentVariables("PROPRESENTER_UTILITIES_");

// Integrate with the platform service manager. Both calls are no-ops when the process is started
// interactively, so one build serves both cases.
builder.Host.UseWindowsService(options => options.ServiceName = "ProPresenter Utilities");
builder.Host.UseSystemd();

// Bound to the loopback address by default: the ProPresenter API itself is unauthenticated, and so
// is this UI, so it should not be exposed to the network unless the operator opts in.
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://127.0.0.1:8752");

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Two clients: a short timeout for commands, none at all for the long-lived timer stream.
builder.Services.AddHttpClient("propresenter", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient("propresenter-stream", client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddSingleton<ProPresenterEndpoint>();

builder.Services.AddSingleton(sp => new ProPresenterClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("propresenter"),
    sp.GetRequiredService<ProPresenterEndpoint>(),
    sp.GetRequiredService<ILogger<ProPresenterClient>>()));

builder.Services.AddSingleton(sp => new TimerStream(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("propresenter-stream"),
    sp.GetRequiredService<ProPresenterEndpoint>(),
    sp.GetRequiredService<ILogger<TimerStream>>()));

// FOH lights: a Shelly relay, switched by the lights.* actions, plus the watcher that brings the
// lights back when a video that turned them off ends.
builder.Services.AddSingleton<ShellyClient>();
builder.Services.AddSingleton<LightsController>();
builder.Services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<LightsController>());
builder.Services.AddHostedService<MediaWatcher>();

builder.Services.AddSingleton<ActionExecutor>();
builder.Services.AddSingleton<ActivityLog>();
builder.Services.AddSingleton(sp => new ConfigurationStore(
    sp.GetRequiredService<ILogger<ConfigurationStore>>(),
    builder.Configuration["Config"]));
builder.Services.AddSingleton<TriggerEngine>();
builder.Services.AddHostedService<TriggerEngineHostedService>();

// Inbound signals: RossTalk from ProPresenter macros, plus the HTTP webhook below.
builder.Services.AddSingleton<SignalBus>();
builder.Services.AddSingleton<RossTalkListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RossTalkListener>());
builder.Services.AddSingleton<UiActivity>();
builder.Services.AddSingleton<LookupCache>();
builder.Services.AddHostedService<LookupRefreshService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Universal signal entry point for anything that can make an HTTP request — Companion, a Stream Deck,
// a shell script, another automation system. ProPresenter macros cannot call HTTP themselves, so they
// use the RossTalk listener instead; this is the escape hatch for everything else.
// GET is supported as well as POST because many simple senders can only issue a GET.
app.MapMethods("/api/signals/{name}", ["GET", "POST"], (string name, SignalBus signals, HttpContext http) =>
    {
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest("A signal name is required.");

        var remote = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        signals.Publish($"webhook:{name}", $"HTTP from {remote}");

        return Results.Ok(new { signal = $"webhook:{name}" });
    })
    // No form payload is read, and requiring an antiforgery token would defeat the purpose.
    .DisableAntiforgery();

// Construct the engine eagerly so it is subscribed to the stream before the first tick arrives,
// rather than when a browser first connects.
_ = app.Services.GetRequiredService<TriggerEngine>();

app.Run();
