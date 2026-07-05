using System.Diagnostics;

namespace Vidar.Communication.Bambu;

public static class BambuSnapshot
{
    public static string RtspUrl(string host, string accessCode) =>
        $"rtsps://bblp:{accessCode}@{host}:322/streaming/live/1";

    // Downscale so a JPEG stays well under Akka.Remote's 128 KB default max-frame-size,
    // since the frame is asked for across the cluster boundary (worker -> host).
    public static string[] FfmpegArgs(string host, string accessCode) =>
    [
        "-nostdin", "-loglevel", "error",
        "-rtsp_transport", "tcp",
        "-i", RtspUrl(host, accessCode),
        "-frames:v", "1",
        "-vf", "scale=640:-1",
        "-q:v", "8",
        "-f", "image2",
        "pipe:1",
    ];

    public static async Task<byte[]?> CaptureAsync(string host, string accessCode, TimeSpan timeout, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };
        foreach (var a in FfmpegArgs(host, accessCode)) proc.StartInfo.ArgumentList.Add(a);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            proc.Start();
            using var ms = new MemoryStream();
            await proc.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
        }
        catch
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return null;
        }
    }
}
