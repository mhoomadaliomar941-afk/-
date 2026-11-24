using System.Net;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using Xunit;
using J = OrganizationEmpire.Core.Json.Json;

namespace OrganizationEmpire.Core.Tests;

public class PromptTests
{
    private static NpcContext MemberContext(FactionType type)
    {
        var ctx = TestHelpers.NewGame(type, 4);
        var m = ctx.Faction.Members[0];
        m.Opinions[ctx.Faction.Members[1].Id] = 80;
        MemoryStore.AddEntry(ctx.State, m.Id, m.FullName, MemoryKind.Promotion, "Was promoted after the docks job.", 0.9f, 0, 40);
        var c = ContextBuilder.ForMember(ctx, m, new AiSettings());
        c.Location = "Mirror Park";
        return c;
    }

    [Fact]
    public void SystemPromptContainsContextAndRules()
    {
        var c = MemberContext(FactionType.Militia);
        string p = PromptBuilder.BuildSystemPrompt(c, new AiSettings { MaxReplyWords = 55 });
        Assert.Contains(c.Name, p);
        Assert.Contains("Commander", p);               // player title
        Assert.Contains("paramilitary", p);            // militia voice
        Assert.Contains("never more than 55 words", p);
        Assert.Contains("Never give real-world instructions", p);
        Assert.Contains("promoted after the docks job", p);
        Assert.Contains("close friend", p);
        Assert.Contains("Mirror Park", p);
    }

    [Fact]
    public void VoicesDifferByFactionType()
    {
        string gang = PromptBuilder.StyleGuide(MemberContext(FactionType.StreetGang));
        string mafia = PromptBuilder.StyleGuide(MemberContext(FactionType.Mafia));
        string mil = PromptBuilder.StyleGuide(MemberContext(FactionType.Militia));
        Assert.Contains("slang", gang);
        Assert.Contains("Never use street-gang slang", mafia);
        Assert.Contains("Never use gang slang", mil);
        Assert.NotEqual(gang, mafia);
        Assert.NotEqual(mafia, mil);
    }

    [Fact]
    public void RequestKeepsOnlyConfiguredHistory()
    {
        var c = MemberContext(FactionType.Mafia);
        var history = Enumerable.Range(0, 30).Select(i => new ChatTurn { Speaker = i % 2 == 0 ? "player" : "npc", Text = "t" + i }).ToList();
        var req = PromptBuilder.BuildRequest(c, history, "Report.", new AiSettings { MemoryTurns = 3, Provider = AiProviderKind.Anthropic, Temperature = 0.9f });
        Assert.Equal(7, req.Messages.Count);
        Assert.Equal("Report.", req.Messages.Last().Content);
        Assert.True(req.Temperature < 0); // never sent to Anthropic
    }

    [Fact]
    public void ParserStripsTagsAndMarkup()
    {
        var r = ResponseParser.Parse("Tony: \"*nods* Sure thing, ~r~boss. [[MOOD:+1]]\"", "Tony Moretti");
        Assert.Equal("(nods) Sure thing, -r-boss.", r.Text);
        Assert.Equal(1, r.MoodDelta);
        var w = ResponseParser.Parse("Alright, I'm in. [[WILLING]]", "X");
        Assert.True(w.Willing);
        Assert.Equal("Alright, I'm in.", w.Text);
    }

    [Theory]
    [InlineData("follow me", OrderType.Follow)]
    [InlineData("Stay here and wait", OrderType.Stay)]
    [InlineData("go back to hq now", OrderType.ReturnToHQ)]
    [InlineData("get in the car", OrderType.EnterVehicle)]
    [InlineData("cover me!", OrderType.DefendPlayer)]
    public void DetectsOrders(string text, OrderType expected)
    {
        Assert.Equal(expected, IntentDetector.DetectOrder(text));
    }

    [Fact]
    public void DoesNotDetectOrdersInSmallTalk()
    {
        Assert.Null(IntentDetector.DetectOrder("how is your family doing?"));
    }

    [Fact]
    public void OfflineRepliesDifferByStyle()
    {
        var rng = new OrganizationEmpire.Core.Util.Rng(1);
        string gang = OfflineDialogueProvider.Generate(MemberContext(FactionType.StreetGang), "give me a report", rng);
        string mil = OfflineDialogueProvider.Generate(MemberContext(FactionType.Militia), "give me a report", new OrganizationEmpire.Core.Util.Rng(1));
        Assert.Contains("Real talk", gang);
        Assert.Contains("Sitrep", mil);
    }
}

public class MemoryTests
{
    [Fact]
    public void EntriesAreTrimmedIntoSummary()
    {
        var s = new GameState();
        for (int i = 0; i < 20; i++) MemoryStore.AddEntry(s, "a", "A", MemoryKind.Event, "event " + i, i == 3 ? 1f : 0.1f, i, 5);
        var m = s.Memories["a"];
        Assert.Equal(5, m.Entries.Count);
        Assert.Contains(m.Entries, e => e.Text == "event 3"); // important memory kept
        Assert.False(string.IsNullOrEmpty(m.Summary));
        Assert.True(m.Summary.Length <= MemoryStore.MaxSummaryChars);
    }

    [Fact]
    public void DialogueIsCapped()
    {
        var s = new GameState();
        for (int i = 0; i < 100; i++) MemoryStore.AddDialogue(s, "a", "A", i % 2 == 0 ? "player" : "npc", "line " + i, 0);
        Assert.Equal(MemoryStore.MaxDialogueTurns, s.Memories["a"].Dialogue.Count);
        Assert.Contains("Talked about", s.Memories["a"].Summary);
    }

    [Fact]
    public void CivilianMemoriesAreLimited()
    {
        var s = new GameState();
        for (int i = 0; i < 50; i++) MemoryStore.Get(s, "npc:" + i, "x").LastTalkedDay = i;
        Assert.True(s.Memories.Count <= 31);
    }
}

public class ProviderTests
{
    private static ChatRequest Req() => new ChatRequest
    {
        SystemPrompt = "sys",
        MaxTokens = 100,
        Temperature = 0.7f,
        Messages = { new ChatMessage(ChatRole.Assistant, "earlier"), new ChatMessage(ChatRole.User, "hello") },
    };

    private static ProviderOptions Opt(AiProviderKind kind, string endpoint, string model = "m", string key = "test-key-123456") =>
        new ProviderOptions { Kind = kind, Endpoint = endpoint, Model = model, ApiKey = key, TimeoutSeconds = 5 };

    [Fact]
    public async Task OpenAICompatibleSendsStandardRequest()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"model\":\"m\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Yo boss.\"},\"finish_reason\":\"stop\"}]}");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "https://example.test/v1/"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("Yo boss.", r.Text);
        var (req, body) = h.Requests[0];
        Assert.Equal("https://example.test/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer test-key-123456", req.Headers.Authorization!.ToString());
        var tree = J.Parse(body);
        Assert.Equal("m", J.GetString(tree, "model"));
        Assert.Equal(100L, J.Get(tree, "max_tokens"));
        Assert.Equal("system", J.GetString(((List<object>)J.Get(tree, "messages")!)[0], "role"));
    }

    [Fact]
    public async Task OpenAICompatibleRetriesWithoutRejectedTemperature()
    {
        var h = new FakeHandler()
            .Enqueue(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Unsupported value: 'temperature' does not support 0.7\"}}")
            .Enqueue(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "https://x.test/v1"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal(2, h.Requests.Count);
        Assert.Null(J.Get(J.Parse(h.Requests[1].body), "temperature"));
    }

    [Fact]
    public async Task LocalServerWithoutKeySendsNoAuthHeader()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"part\"}]}}]}");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "http://localhost:11434/v1", "llama3.1", ""), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal("part", r.Text);
        Assert.Null(h.Requests[0].req.Headers.Authorization);
    }

    [Fact]
    public async Task AnthropicUsesMessagesApiShape()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.OK,
            "{\"model\":\"claude-opus-5-5\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":\"Consider it done.\"}]}");
        var p = new AnthropicProvider(Opt(AiProviderKind.Anthropic, "", AnthropicProvider.DefaultModel), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("Consider it done.", r.Text);
        var (req, body) = h.Requests[0];
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("test-key-123456", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal(AnthropicProvider.ApiVersion, req.Headers.GetValues("anthropic-version").Single());
        Assert.Equal(AnthropicProvider.FallbackBeta, req.Headers.GetValues("anthropic-beta").Single());
        var tree = J.Parse(body);
        Assert.Equal("sys", J.GetString(tree, "system"));
        Assert.Null(J.Get(tree, "temperature"));
        Assert.Equal("default", J.GetString(tree, "fallbacks"));
        Assert.Equal("low", J.GetString(J.Get(tree, "output_config"), "effort"));
        var msgs = (List<object>)J.Get(tree, "messages")!;
        Assert.Equal("user", J.GetString(msgs[0], "role")); // conversation must start with the user
        Assert.Equal("user", J.GetString(msgs[^1], "role"));
    }

    [Fact]
    public async Task AnthropicRefusalAndAdaptiveRetry()
    {
        var h = new FakeHandler()
            .Enqueue(HttpStatusCode.BadRequest, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"fallbacks: not supported for this model\"}}")
            .Enqueue(HttpStatusCode.OK, "{\"stop_reason\":\"refusal\",\"content\":[],\"stop_details\":{\"type\":\"refusal\",\"explanation\":\"policy\"}}");
        var p = new AnthropicProvider(Opt(AiProviderKind.Anthropic, "https://api.anthropic.com/v1"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Equal(AiErrorKind.Refused, r.Error);
        Assert.Equal(2, h.Requests.Count);
        Assert.False(h.Requests[1].req.Headers.Contains("anthropic-beta"));
        Assert.Null(J.Get(J.Parse(h.Requests[1].body), "fallbacks"));
    }

    [Fact]
    public async Task GeminiParsesCandidates()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi \"},{\"text\":\"there\"}]},\"finishReason\":\"STOP\"}]}");
        var p = new GeminiProvider(Opt(AiProviderKind.Gemini, "", "gemini-x"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal("hi there", r.Text);
        Assert.Contains("/models/gemini-x:generateContent", h.Requests[0].req.RequestUri!.ToString());
        Assert.DoesNotContain("key=", h.Requests[0].req.RequestUri!.ToString());
        Assert.Equal("model", J.GetString(((List<object>)J.Get(J.Parse(h.Requests[0].body), "contents")!)[1], "role"));
    }

    [Theory]
    [InlineData(401, AiErrorKind.InvalidKey)]
    [InlineData(403, AiErrorKind.InvalidKey)]
    [InlineData(500, AiErrorKind.ServerError)]
    [InlineData(529, AiErrorKind.ServerError)]
    [InlineData(404, AiErrorKind.BadRequest)]
    public async Task MapsHttpErrors(int status, AiErrorKind kind)
    {
        var h = new FakeHandler().Enqueue((HttpStatusCode)status, "{\"error\":{\"message\":\"nope\"}}");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "https://x.test/v1"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Equal(kind, r.Error);
        Assert.Contains("nope", r.ErrorMessage);
    }

    [Fact]
    public async Task RateLimitRetriesOnce()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.TooManyRequests, "{}").Enqueue(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "https://x.test/v1"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task TimeoutIsReported()
    {
        var h = new FakeHandler { OnSend = (_, ct) => Task.Delay(TimeSpan.FromSeconds(30), ct) };
        var o = Opt(AiProviderKind.OpenAICompatible, "https://x.test/v1");
        o.TimeoutSeconds = 5;
        var p = new OpenAICompatibleProvider(o, new HttpClient(h));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Equal(AiErrorKind.Timeout, r.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task MalformedJsonIsReported()
    {
        var h = new FakeHandler().Enqueue(HttpStatusCode.OK, "<html>gateway</html>");
        var p = new OpenAICompatibleProvider(Opt(AiProviderKind.OpenAICompatible, "https://x.test/v1"), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.Equal(AiErrorKind.MalformedResponse, r.Error);
    }

    [Fact]
    public async Task MissingConfigurationIsReportedWithoutNetwork()
    {
        var h = new FakeHandler();
        var p = new AnthropicProvider(Opt(AiProviderKind.Anthropic, "", "claude-opus-5-5", ""), new HttpClient(h));
        var r = await p.CompleteAsync(Req(), CancellationToken.None);
        Assert.Equal(AiErrorKind.NotConfigured, r.Error);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public void ErrorMessagesNeverContainKeys()
    {
        OrganizationEmpire.Core.Util.SecretRedactor.Register("sk-live-verysecretvalue1234");
        var r = HttpChatProvider.MapError(401, "{\"error\":{\"message\":\"Incorrect API key provided: sk-live-verysecretvalue1234\"}}");
        Assert.DoesNotContain("verysecretvalue1234", r.ErrorMessage);
    }

    [Fact]
    public void KeyResolverReadsEnvironment()
    {
        Environment.SetEnvironmentVariable("OE_TEST_KEY", "env-secret-value");
        Assert.Equal("env-secret-value", KeyResolver.Resolve("env:OE_TEST_KEY", null, out string src));
        Assert.Equal("environment variable", src);
        Assert.Equal("", KeyResolver.Resolve("dpapi:abc", null, out _));
        Assert.Equal("plain", KeyResolver.Resolve(" plain ", null, out _));
    }
}

public class DialogueServiceTests
{
    [Fact]
    public async Task RemoteFailureFallsBackOfflineAndUpdatesMemoryOnPump()
    {
        var ctx = TestHelpers.NewGame(FactionType.StreetGang, 3);
        var settings = new AiSettings { Provider = AiProviderKind.OpenAICompatible, Endpoint = "https://x.test/v1", Model = "m", MinSecondsBetweenRequests = 0 };
        var h = new FakeHandler().Enqueue(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"bad key\"}}");
        var svc = new DialogueService(() => settings, () => new OpenAICompatibleProvider(ProviderOptions.From(settings, "k-123456"), new HttpClient(h)));
        var m = ctx.Faction.Members[0];
        var conv = svc.Open(ctx.State, m.Id, m.FullName, true, false);
        Assert.True(svc.Send(conv, "follow me, we got work", ContextBuilder.ForMember(ctx, m, settings), ctx.State, out string err), err);
        Assert.Equal(OrderType.Follow, conv.SuggestedOrder);
        Assert.False(svc.Send(conv, "again", ContextBuilder.ForMember(ctx, m, settings), ctx.State, out _)); // still waiting

        DialogueResult? applied = null;
        for (int i = 0; i < 100 && applied == null; i++)
        {
            await Task.Delay(20);
            svc.Pump(ctx.State, r => applied = r);
        }
        Assert.NotNull(applied);
        Assert.True(applied!.UsedFallback);
        Assert.Equal(ConversationState.Idle, conv.State);
        Assert.Contains(conv.Visible, t => t.Speaker == "npc");
        Assert.Contains(conv.Visible, t => t.Speaker == "system" && t.Text.Contains("rejected"));
        Assert.Equal(2, ctx.State.Memories[m.Id].Dialogue.Count);
    }

    [Fact]
    public async Task OfflineConversationWorksWithoutNetwork()
    {
        var ctx = TestHelpers.NewGame(FactionType.Mafia, 3);
        var settings = new AiSettings { Provider = AiProviderKind.Offline, MinSecondsBetweenRequests = 0 };
        var svc = new DialogueService(() => settings, () => ProviderFactory.Create(settings, ""));
        var m = ctx.Faction.Members[0];
        var conv = svc.Open(ctx.State, m.Id, m.FullName, true, true);
        Assert.True(svc.Send(conv, "How's morale?", ContextBuilder.ForMember(ctx, m, settings), ctx.State, out _));
        DialogueResult? applied = null;
        for (int i = 0; i < 100 && applied == null; i++) { await Task.Delay(10); svc.Pump(ctx.State, r => applied = r); }
        Assert.NotNull(applied);
        Assert.False(applied!.UsedFallback);
        Assert.False(string.IsNullOrWhiteSpace(applied.Reply.Text));
    }
}
