using System.Buffers.Binary;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

/// <summary>Real HTTP/2 and gRPC wire frames, generated production clients; no production server code.</summary>
public class GrpcTransportTests
{
    [Fact]
    public async Task GeneratedClientsSendOnlyAccessTokenAndPreserveServerData()
    {
        await using var fixture = await GrpcFixture.Start();
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        var ping = await backend.PingAsync(CancellationToken.None);
        Assert.Equal("v1-fixture", ping.ApiVersion);
        var overview = await backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None);
        Assert.Equal(DayPhase.Running, overview.Day.Status.Phase);
        Assert.Equal(71, overview.Day.Status.Version);
        Assert.Equal("02", overview.Races.Races[0].Definition.Runners[0].ProgramNumber);
        Assert.Equal(RacePhase.BettingClosed, overview.Races.Races[0].Status.Phase);
        Assert.Equal(42, overview.Races.Races[0].Status.Version);
        Assert.Equal(4, fixture.Calls.Count);
        Assert.Empty(fixture.Calls[0].Auth);
        Assert.All(fixture.Calls.Skip(1), c => Assert.Equal(new[] { "Bearer access-only" }, c.Auth));
        Assert.All(fixture.Calls, c => { Assert.Equal("HTTP/2", c.Protocol); Assert.False(string.IsNullOrEmpty(c.Timeout)); });
        await Assert.ThrowsAsync<ArgumentException>(() => backend.ReadAsync(Guid.NewGuid(), "access-only", CancellationToken.None));
        Assert.Equal(4, fixture.Calls.Count);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task RealTrailersRetainStatus(StatusCode code)
    {
        await using var fixture = await GrpcFixture.Start(code);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None));
        Assert.Equal(code, error.StatusCode);
    }

    [Fact] public async Task CancellationReachesTransport()
    {
        await using var fixture = await GrpcFixture.Start(delay: true);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.PingAsync(cancel.Token));
        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
    }

    [Fact] public async Task DeadlineActuallyExpires()
    {
        await using var fixture = await GrpcFixture.Start(delay: true);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true), TimeSpan.FromMilliseconds(150));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.PingAsync(CancellationToken.None));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
    }

    [Fact] public async Task RedirectDoesNotForwardBearerToAnotherOrigin()
    {
        await using var other = await GrpcFixture.Start();
        await using var origin = await GrpcFixture.Start(redirect: other.Address);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(origin.Address, development: true));
        await Assert.ThrowsAsync<RpcException>(() => backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None));
        Assert.Single(origin.Calls);
        Assert.Empty(other.Calls);
    }

    private sealed class GrpcFixture(WebApplication app) : IAsyncDisposable
    {
        public string Address => app.Urls.Single();
        public List<(string[] Auth, string Timeout, string Protocol)> Calls = [];
        public static async Task<GrpcFixture> Start(StatusCode? error = null, bool delay = false, string? redirect = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0, p => p.Protocols = HttpProtocols.Http2));
            var app = builder.Build();
            var fixture = new GrpcFixture(app);
            app.Run(async context =>
            {
                fixture.Calls.Add((context.Request.Headers.Authorization.Select(s => s!).ToArray(),
                    context.Request.Headers["grpc-timeout"].ToString(), context.Request.Protocol));
                if (redirect is not null) { context.Response.StatusCode = 307; context.Response.Headers.Location = redirect + context.Request.Path; return; }
                context.Response.ContentType = "application/grpc";
                if (delay) { await Task.Delay(30000, context.RequestAborted); return; }
                if (error is not null)
                {
                    context.Response.Headers["grpc-status"] = ((int)error).ToString();
                    return;
                }
                using var input = new MemoryStream();
                await context.Request.Body.CopyToAsync(input);
                var wire = input.ToArray();
                var method = context.Request.Path.Value!.Split('/').Last();
                IMessage result;
                if (method == "Ping") result = new PingResponse { Status = "OK", ApiVersion = "v1-fixture" };
                else
                {
                    var query = method == "GetRace" ? RaceQuery.Parser.ParseFrom(wire.AsSpan(5)).RaceDayId : DayQuery.Parser.ParseFrom(wire.AsSpan(5)).RaceDayId;
                    Assert.Equal(ConnectionTests.Day.ToString(), query);
                    var race = new RaceInformation
                    {
                        Definition = new RaceConfiguration { RaceId = "ad238129-b219-4a6e-908f-a5da6c73e932", Runners = { new Runner { HorseId = "horse", ProgramNumber = "02" } } },
                        Status = new RaceAcknowledgement { Phase = RacePhase.BettingClosed, Version = 42 }
                    };
                    result = method switch
                    {
                        "GetRaceDay" => new RaceDayInformation { Definition = new RaceDayConfiguration { RaceDayId = query }, Status = new DayAcknowledgement { Phase = DayPhase.Running, Version = 71 } },
                        "ListRaces" => new RaceList { Races = { race } },
                        "GetRace" => race,
                        _ => throw new InvalidOperationException("Unexpected RPC")
                    };
                }
                var bytes = result.ToByteArray();
                var frame = new byte[bytes.Length + 5];
                BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), bytes.Length);
                bytes.CopyTo(frame, 5);
                context.Response.DeclareTrailer("grpc-status");
                await context.Response.Body.WriteAsync(frame);
                context.Response.AppendTrailer("grpc-status", "0");
            });
            await app.StartAsync();
            return fixture;
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
