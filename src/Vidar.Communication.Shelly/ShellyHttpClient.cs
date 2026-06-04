using System.Text.Json;

namespace Vidar.Communication.Shelly;

public sealed class ShellyHttpClient(HttpClient httpClient)
{
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
}
