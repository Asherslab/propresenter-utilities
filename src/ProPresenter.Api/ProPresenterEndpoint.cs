namespace ProPresenter.Api;

/// <summary>
/// Where to find ProPresenter. Mutable at runtime so the host/port can be changed from the UI
/// without restarting the process.
/// </summary>
public sealed class ProPresenterEndpoint
{
    private volatile Uri _baseUri = new("http://localhost:54937/");

    public Uri BaseUri
    {
        get => _baseUri;
        private set => _baseUri = value;
    }

    public string Host => BaseUri.Host;
    public int Port => BaseUri.Port;

    /// <summary>Raised after <see cref="Set"/> changes the endpoint, so long-lived streams can reconnect.</summary>
    public event Action? Changed;

    public void Set(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

        var candidate = new UriBuilder("http", host.Trim(), port).Uri;
        if (candidate == BaseUri) return;

        BaseUri = candidate;
        Changed?.Invoke();
    }

    public Uri Resolve(string relativePath) => new(BaseUri, relativePath.TrimStart('/'));
}
