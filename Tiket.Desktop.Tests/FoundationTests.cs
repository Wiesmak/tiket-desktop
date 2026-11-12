using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Net.Client;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void NavigationNotifiesOnlyOnChangeAndNeverEnablesSubmission()
    {
        var state = new ShellState();
        var changes = 0;
        state.PropertyChanged += (_, args) => { Assert.Equal("Destination", args.PropertyName); changes++; };
        Assert.Equal(Destination.Cashier, state.Destination);
        foreach (var destination in new[] { Destination.Administration, Destination.Connection, Destination.Cashier })
        {
            state.Navigate(destination);
            state.Navigate(destination);
            Assert.Equal(destination, state.Destination);
            Assert.False(state.CanSubmit);
        }
        Assert.Equal(3, changes);
        Assert.Contains("Unconnected", state.ConnectionStatus);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Navigate((Destination)99));
    }

    [Theory]
    [InlineData("https://backend.example.org", false, true)]
    [InlineData("http://127.0.0.1:9090", true, true)]
    [InlineData("http://127.0.0.1:9090", false, false)]
    [InlineData("http://remote.example.org", true, false)]
    [InlineData("https://user:secret@backend.example.org", false, false)]
    [InlineData("https://backend.example.org?token=secret", false, false)]
    [InlineData("file:///C:/backend", false, false)]
    [InlineData("", false, false)]
    public void EndpointPolicy(string endpoint, bool development, bool expected) =>
        Assert.Equal(expected, ConnectionConfiguration.IsValidEndpoint(endpoint, development));

    [Fact]
    public void GeneratedPingClientAndMessagesCompileWithoutCallingBackend()
    {
        using var channel = GrpcChannel.ForAddress("https://127.0.0.1:1");
        var client = new SystemService.SystemServiceClient(channel);
        Assert.NotNull(client);
        var response = new PingResponse { Status = "demo", ApiVersion = "v1" };
        Assert.Equal(response, PingResponse.Parser.ParseFrom(response.ToByteArray()));
        Assert.Empty(new PingRequest().ToByteArray());
    }

    [Fact]
    public void SnapshotHasExactlyFourOriginalFilesAndAllEightServices()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "contracts");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "snapshot.json")));
        var files = manifest.RootElement.GetProperty("files");
        Assert.Equal(4, files.EnumerateObject().Count());
        Assert.Equal(4, Directory.GetFiles(Path.Combine(root, "upstream"), "*.proto", SearchOption.AllDirectories).Length);
        var combined = "";
        foreach (var file in files.EnumerateObject())
        {
            var path = Path.Combine(root, "upstream", file.Name);
            Assert.Equal(file.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            combined += File.ReadAllText(path);
        }
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(combined, @"\bservice\s+").Count);
        Assert.Equal(37, System.Text.RegularExpressions.Regex.Matches(combined, @"\brpc\s+").Count);
    }
}
