using System.Net.Sockets;
using AmbientDirector.Api.Errors;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class ErrorClassifierTests
{
    // Issue #90: a missing storage file must NOT be mistaken for a bulb timeout. FileNotFoundException and
    // DirectoryNotFoundException both derive from IOException, so this locks in the arm ORDER — the specific
    // storage arm has to win over the socket/IO/timeout "bulb unreachable" arm that follows it.
    [Fact]
    public void Missing_file_maps_to_500_storage_not_504_bulb()
    {
        var (status, titleKey) = ErrorClassifier.Classify(new FileNotFoundException("Could not find file: ./imgs/x.webp"));

        Assert.Equal(500, status);
        Assert.Equal("error.title.storage", titleKey);
    }

    [Fact]
    public void Missing_directory_maps_to_500_storage_not_504_bulb()
    {
        var (status, titleKey) = ErrorClassifier.Classify(new DirectoryNotFoundException("Could not find a part of the path './imgs'."));

        Assert.Equal(500, status);
        Assert.Equal("error.title.storage", titleKey);
    }

    // Regression guard for the arm below the new one: a generic IO/socket/timeout fault still means the
    // device is unreachable (the classic Tuya/Hue transport failure), so it stays a 504.
    [Theory]
    [MemberData(nameof(BulbTimeoutExceptions))]
    public void Generic_transport_faults_stay_504_bulb(Exception ex)
    {
        var (status, titleKey) = ErrorClassifier.Classify(ex);

        Assert.Equal(504, status);
        Assert.Equal("error.title.bulbUnreachable", titleKey);
    }

    public static TheoryData<Exception> BulbTimeoutExceptions() =>
    [
        new IOException("pipe broke"),
        new SocketException(),
        new TimeoutException(),
    ];
}
