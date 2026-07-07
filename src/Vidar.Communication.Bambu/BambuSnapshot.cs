using System.Diagnostics;

namespace Vidar.Communication.Bambu;

public static class BambuSnapshot
{
    // A capture either yields a JPEG or a short human-readable reason it failed (from ffmpeg's
    // stderr), so the worker can log WHY a snapshot didn't come through instead of failing silently.
    public sealed record CaptureResult(byte[]? Jpeg, string? Error);

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

    public static async Task<CaptureResult> CaptureAsync(string host, string accessCode, TimeSpan timeout, CancellationToken ct)
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
            // Drain both streams concurrently to avoid a pipe-buffer deadlock.
            var stdoutTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode == 0 && ms.Length > 0)
                return new CaptureResult(ms.ToArray(), null);

            return new CaptureResult(null, Summarize(await stderrTask, proc.ExitCode));
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return new CaptureResult(null, $"timed out after {timeout.TotalSeconds:0}s (no frame from the printer)");
        }
        catch (Exception ex)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            return new CaptureResult(null, ex.Message);
        }
    }

    // Reduce ffmpeg's stderr to its last meaningful line (e.g. "Connection refused") for a clean log.
    private static string Summarize(string stderr, int exitCode)
    {
        var last = stderr
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0);
        var msg = string.IsNullOrEmpty(last) ? $"ffmpeg exited with code {exitCode}" : last;
        return msg.Length > 240 ? msg[..240] : msg;
    }
}
