using Vidar.Communication.Bambu;
using Xunit;

namespace Vidar.Communication.Bambu.Tests;

public class BambuSnapshotTests
{
    [Fact]
    public void RtspUrl_UsesBblpAndPort322() =>
        Assert.Equal("rtsps://bblp:abc123@192.168.1.50:322/streaming/live/1",
            BambuSnapshot.RtspUrl("192.168.1.50", "abc123"));

    [Fact]
    public void FfmpegArgs_GrabsOneDownscaledJpegToStdout()
    {
        var args = BambuSnapshot.FfmpegArgs("192.168.1.50", "abc123");
        Assert.Contains("-frames:v", args);
        Assert.Contains("scale=640:-1", string.Join(" ", args));
        Assert.Equal("pipe:1", args[^1]);
    }
}
