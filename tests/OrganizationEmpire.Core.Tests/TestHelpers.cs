using System.Net;
using System.Text;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Tests;

internal static class TestHelpers
{
    public static SimContext NewGame(FactionType type, int members = 4, StartingProgression prog = StartingProgression.Humble, int seed = 42, bool hq = true)
    {
        var state = Simulation.NewGame(seed);
        var cfg = new ModConfig();
        var ctx = new SimContext(state, cfg, new Rng(seed));
        var opts = new FactionCreationOptions
        {
            Name = "Test " + type,
            Type = type,
            InitialMembers = members,
            Progression = prog,
            HasHeadquarters = hq,
            HqZone = "MIRR",
            HqPosition = new Vec3(1100, -550, 57),
            Activities = type == FactionType.StreetGang ? GangActivities.Both : GangActivities.None,
        };
        state.Faction = FactionFactory.Create(state, opts, ctx.Rng);
        return ctx;
    }

    public static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "oe_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}

/// <summary>Records requests and returns canned responses.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public readonly List<(HttpRequestMessage req, string body)> Requests = new();
    private readonly Queue<(HttpStatusCode code, string body)> _responses = new();
    public Func<HttpRequestMessage, CancellationToken, Task>? OnSend;

    public FakeHandler Enqueue(HttpStatusCode code, string body)
    {
        _responses.Enqueue((code, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (OnSend != null) await OnSend(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var (code, resp) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.InternalServerError, "{}");
        return new HttpResponseMessage(code) { Content = new StringContent(resp, Encoding.UTF8, "application/json") };
    }
}
