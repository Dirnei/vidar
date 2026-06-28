using System.Text.Json;

namespace Vidar.Communication.Shelly;

public sealed class ShellyHttpClient(HttpClient httpClient)
{
    // ── Gen2 (RPC) ────────────────────────────────────────────────────────────

    public async Task<JsonDocument?> GetStatusAsync(string host)
    {
        var response = await httpClient.GetAsync($"http://{host}/rpc/Shelly.GetStatus");
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    public async Task<JsonDocument?> GetDeviceInfoAsync(string host)
    {
        try
        {
            var response = await httpClient.GetAsync($"http://{host}/rpc/Shelly.GetDeviceInfo");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetSwitchAsync(string host, int channel, bool on)
    {
        var url = $"http://{host}/rpc/Switch.Set?id={channel}&on={on.ToString().ToLowerInvariant()}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetCoverPositionAsync(string host, int channel, int position)
    {
        var url = $"http://{host}/rpc/Cover.GoToPosition?id={channel}&pos={position}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetLightAsync(string host, int channel, bool? on, int? brightness)
    {
        var url = $"http://{host}/rpc/Light.Set?id={channel}";
        if (on.HasValue)
            url += $"&on={on.Value.ToString().ToLowerInvariant()}";
        if (brightness.HasValue)
            url += $"&brightness={brightness.Value}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    // ── Gen1 (REST) ───────────────────────────────────────────────────────────

    public async Task<JsonDocument?> GetGen1StatusAsync(string host)
    {
        try
        {
            var response = await httpClient.GetAsync($"http://{host}/status");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch
        {
            return null;
        }
    }

    public async Task Gen1SetSwitchAsync(string host, int channel, bool on)
    {
        var url = $"http://{host}/relay/{channel}?turn={(on ? "on" : "off")}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> Gen1SetCoverPositionAsync(string host, int position)
    {
        var url = $"http://{host}/roller/0?go=to_pos&roller_pos={position}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task Gen1SetLightAsync(string host, int channel, bool? on, int? brightness)
    {
        var query = new List<string>();
        if (on.HasValue)
            query.Add($"turn={(on.Value ? "on" : "off")}");
        if (brightness.HasValue)
            query.Add($"brightness={brightness.Value}");
        var url = $"http://{host}/light/{channel}?{string.Join("&", query)}";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task Gen1OpenCoverAsync(string host)
    {
        var url = $"http://{host}/roller/0?go=open";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task Gen1CloseCoverAsync(string host)
    {
        var url = $"http://{host}/roller/0?go=close";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }
}
