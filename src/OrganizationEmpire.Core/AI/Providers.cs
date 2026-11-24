using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Util;
using J = OrganizationEmpire.Core.Json.Json;

namespace OrganizationEmpire.Core.AI
{
    public enum ChatRole { System, User, Assistant }

    public sealed class ChatMessage
    {
        public ChatRole Role;
        public string Content;
        public ChatMessage(ChatRole role, string content) { Role = role; Content = content ?? ""; }
    }

    public sealed class ChatRequest
    {
        public string SystemPrompt = "";
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public int MaxTokens = 220;
        /// <summary>Negative = do not send.</summary>
        public float Temperature = -1;
        /// <summary>Structured context for the offline engine; ignored by HTTP providers.</summary>
        public NpcContext Context;
    }

    public enum AiErrorKind { None, NotConfigured, InvalidKey, RateLimited, Timeout, Network, ServerError, BadRequest, MalformedResponse, Refused, Cancelled, Unknown }

    public sealed class ChatResult
    {
        public bool Success;
        public string Text;
        public AiErrorKind Error;
        public string ErrorMessage;
        public int StatusCode;
        public long LatencyMs;
        public string ServedModel;

        public static ChatResult Ok(string text, string model = null) => new ChatResult { Success = true, Text = text, ServedModel = model };
        public static ChatResult Fail(AiErrorKind kind, string message, int status = 0) => new ChatResult { Success = false, Error = kind, ErrorMessage = message, StatusCode = status };

        public string UserFacingError()
        {
            switch (Error)
            {
                case AiErrorKind.NotConfigured: return "AI not configured: " + ErrorMessage;
                case AiErrorKind.InvalidKey: return "The API key was rejected (check Settings > AI).";
                case AiErrorKind.RateLimited: return "Rate limited by the AI provider; try again shortly.";
                case AiErrorKind.Timeout: return "The AI provider timed out.";
                case AiErrorKind.Network: return "Could not reach the AI provider (network/endpoint).";
                case AiErrorKind.ServerError: return "The AI provider had a server error.";
                case AiErrorKind.BadRequest: return "Request rejected: " + ErrorMessage;
                case AiErrorKind.MalformedResponse: return "Unexpected response from the AI provider.";
                case AiErrorKind.Refused: return "The model declined to answer.";
                case AiErrorKind.Cancelled: return "Request cancelled.";
                default: return "AI error: " + ErrorMessage;
            }
        }
    }

    public interface IChatProvider
    {
        string Name { get; }
        bool IsRemote { get; }
        Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct);
    }

    public sealed class ProviderOptions
    {
        public AiProviderKind Kind;
        public string Endpoint = "";
        public string Model = "";
        public string ApiKey = "";
        public int TimeoutSeconds = 30;
        public string AuthHeader = "Authorization";
        public string AnthropicEffort = "low";
        public bool AnthropicFallbacks = true;

        public static ProviderOptions From(AiSettings s, string resolvedKey)
        {
            return new ProviderOptions
            {
                Kind = s.Provider,
                Endpoint = (s.Endpoint ?? "").Trim(),
                Model = (s.Model ?? "").Trim(),
                ApiKey = resolvedKey ?? "",
                TimeoutSeconds = s.TimeoutSeconds,
                AuthHeader = string.IsNullOrWhiteSpace(s.AuthHeader) ? "Authorization" : s.AuthHeader.Trim(),
                AnthropicEffort = (s.AnthropicEffort ?? "low").Trim().ToLowerInvariant(),
                AnthropicFallbacks = s.AnthropicServerFallbacks,
            };
        }
    }

    /// <summary>Well-known provider presets offered in the settings UI. Model names are editable examples.</summary>
    public static class ProviderPresets
    {
        public sealed class Preset
        {
            public string Name;
            public AiProviderKind Kind;
            public string Endpoint;
            public string Model;
            public bool NeedsKey = true;
            public string Notes;
        }

        public static readonly Preset[] All =
        {
            new Preset { Name = "Offline (built-in)", Kind = AiProviderKind.Offline, Endpoint = "", Model = "", NeedsKey = false, Notes = "No setup, no cost. Template-based dialogue." },
            new Preset { Name = "Anthropic (Claude)", Kind = AiProviderKind.Anthropic, Endpoint = "https://api.anthropic.com", Model = "claude-opus-5-5", Notes = "Most capable default. For faster/cheaper chatter use claude-haiku-5-5." },
            new Preset { Name = "OpenAI", Kind = AiProviderKind.OpenAICompatible, Endpoint = "https://api.openai.com/v1", Model = "", Notes = "Enter a chat model your account can use." },
            new Preset { Name = "OpenRouter", Kind = AiProviderKind.OpenAICompatible, Endpoint = "https://openrouter.ai/api/v1", Model = "", Notes = "Any model id listed on openrouter.ai/models." },
            new Preset { Name = "Groq", Kind = AiProviderKind.OpenAICompatible, Endpoint = "https://api.groq.com/openai/v1", Model = "", Notes = "Fast hosted open models." },
            new Preset { Name = "Google Gemini", Kind = AiProviderKind.Gemini, Endpoint = "https://generativelanguage.googleapis.com/v1beta", Model = "", Notes = "Enter a Gemini model id." },
            new Preset { Name = "Ollama (local)", Kind = AiProviderKind.OpenAICompatible, Endpoint = "http://localhost:11434/v1", Model = "llama3.1", NeedsKey = false, Notes = "Free, runs on your PC. 'ollama pull <model>' first." },
            new Preset { Name = "LM Studio (local)", Kind = AiProviderKind.OpenAICompatible, Endpoint = "http://localhost:1234/v1", Model = "", NeedsKey = false, Notes = "Free, runs on your PC. Load a model and start the server." },
        };
    }

    /// <summary>Shared plumbing: one HttpClient, timeouts, retry on 429, error mapping, adaptive parameter fallback.</summary>
    public abstract class HttpChatProvider : IChatProvider
    {
        private static HttpClient _shared;
        private static readonly object SharedLock = new object();

        protected readonly ProviderOptions Options;
        protected readonly HttpClient Http;

        protected HttpChatProvider(ProviderOptions options, HttpClient http)
        {
            Options = options;
            Http = http ?? SharedClient();
        }

        public abstract string Name { get; }
        public bool IsRemote => true;

        public static HttpClient SharedClient()
        {
            lock (SharedLock)
            {
                if (_shared == null)
                {
#if NETFRAMEWORK
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { /* ignore */ }
#endif
                    _shared = new HttpClient(new HttpClientHandler()) { Timeout = Timeout.InfiniteTimeSpan };
                }
                return _shared;
            }
        }

        protected sealed class Attempt
        {
            public string Url;
            public string Body;
            public Dictionary<string, string> Headers = new Dictionary<string, string>();
        }

        public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            string configError = Validate();
            if (configError != null) return ChatResult.Fail(AiErrorKind.NotConfigured, configError);
            var sw = Stopwatch.StartNew();
            var flags = new HashSet<string>();
            ChatResult result = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var a = Build(request, flags);
                result = await SendAsync(a, ct).ConfigureAwait(false);
                if (result.Success) break;
                if (result.Error == AiErrorKind.RateLimited && attempt == 0 && !flags.Contains("retried429"))
                {
                    flags.Add("retried429");
                    try { await Task.Delay(2500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                    continue;
                }
                // Some models reject optional parameters; drop the offending one and retry once per parameter.
                string adjust = result.Error == AiErrorKind.BadRequest ? AdaptiveFlag(result.ErrorMessage ?? "") : null;
                if (adjust != null && !flags.Contains(adjust))
                {
                    flags.Add(adjust);
                    Log.Info(Name + ": retrying without/with adjusted parameter '" + adjust + "'.");
                    continue;
                }
                break;
            }
            result.LatencyMs = sw.ElapsedMilliseconds;
            return result;
        }

        protected virtual string Validate()
        {
            if (string.IsNullOrWhiteSpace(Options.Model)) return "no model name set.";
            return null;
        }

        /// <summary>Returns a flag naming a parameter to drop/adjust, based on an error message, or null.</summary>
        protected virtual string AdaptiveFlag(string message) => null;

        protected abstract Attempt Build(ChatRequest request, HashSet<string> flags);

        protected abstract ChatResult Parse(object json);

        private async Task<ChatResult> SendAsync(Attempt a, CancellationToken ct)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, Options.TimeoutSeconds))))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
            using (var msg = new HttpRequestMessage(HttpMethod.Post, a.Url))
            {
                msg.Content = new StringContent(a.Body, Encoding.UTF8, "application/json");
                foreach (var h in a.Headers) msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
                HttpResponseMessage resp;
                try
                {
                    resp = await Http.SendAsync(msg, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return ct.IsCancellationRequested ? ChatResult.Fail(AiErrorKind.Cancelled, "cancelled") : ChatResult.Fail(AiErrorKind.Timeout, "timed out after " + Options.TimeoutSeconds + "s");
                }
                catch (HttpRequestException ex)
                {
                    return ChatResult.Fail(AiErrorKind.Network, SecretRedactor.Redact(ex.InnerException?.Message ?? ex.Message));
                }
                catch (Exception ex)
                {
                    return ChatResult.Fail(AiErrorKind.Network, SecretRedactor.Redact(ex.Message));
                }

                using (resp)
                {
                    string text;
                    try { text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
                    catch (Exception ex) { return ChatResult.Fail(AiErrorKind.Network, ex.Message, (int)resp.StatusCode); }
                    int status = (int)resp.StatusCode;
                    if (status >= 200 && status < 300)
                    {
                        object json;
                        try { json = J.Parse(text); }
                        catch (Exception) { return ChatResult.Fail(AiErrorKind.MalformedResponse, "response was not JSON", status); }
                        try
                        {
                            var r = Parse(json);
                            r.StatusCode = status;
                            return r;
                        }
                        catch (Exception ex)
                        {
                            return ChatResult.Fail(AiErrorKind.MalformedResponse, ex.Message, status);
                        }
                    }
                    return MapError(status, text);
                }
            }
        }

        public static ChatResult MapError(int status, string body)
        {
            string detail = ExtractErrorMessage(body);
            detail = SecretRedactor.Redact(TextUtil.Truncate(detail, 300));
            AiErrorKind kind;
            if (status == 401 || status == 403) kind = AiErrorKind.InvalidKey;
            else if (status == 429) kind = AiErrorKind.RateLimited;
            else if (status == 408) kind = AiErrorKind.Timeout;
            else if (status == 529 || status >= 500) kind = AiErrorKind.ServerError;
            else if (status == 400 || status == 404 || status == 413 || status == 422) kind = AiErrorKind.BadRequest;
            else kind = AiErrorKind.Unknown;
            return ChatResult.Fail(kind, "HTTP " + status + (string.IsNullOrEmpty(detail) ? "" : ": " + detail), status);
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            try
            {
                var tree = J.Parse(body);
                var err = J.Get(tree, "error");
                if (err is string s) return s;
                string m = J.GetString(err, "message");
                if (m != null) return m;
                m = J.GetString(tree, "message");
                if (m != null) return m;
                if (tree is List<object> arr && arr.Count > 0) return J.GetString(J.Get(arr[0], "error"), "message") ?? "";
            }
            catch { /* not JSON */ }
            return TextUtil.Truncate(body.Replace("\n", " "), 200);
        }

        protected static string JoinUrl(string baseUrl, string path)
        {
            baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
            if (baseUrl.EndsWith(path, StringComparison.OrdinalIgnoreCase)) return baseUrl;
            return baseUrl + path;
        }

        /// <summary>Merges consecutive same-role turns and ensures the conversation starts with a user turn.</summary>
        protected static List<ChatMessage> NormalizeTurns(List<ChatMessage> input)
        {
            var list = new List<ChatMessage>();
            foreach (var m in input)
            {
                if (m.Role == ChatRole.System || string.IsNullOrWhiteSpace(m.Content)) continue;
                if (list.Count > 0 && list[list.Count - 1].Role == m.Role)
                    list[list.Count - 1] = new ChatMessage(m.Role, list[list.Count - 1].Content + "\n" + m.Content);
                else list.Add(new ChatMessage(m.Role, m.Content));
            }
            if (list.Count == 0 || list[0].Role != ChatRole.User) list.Insert(0, new ChatMessage(ChatRole.User, "(The conversation begins.)"));
            if (list[list.Count - 1].Role != ChatRole.User) list.Add(new ChatMessage(ChatRole.User, "(Go on.)"));
            return list;
        }

        protected static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>OpenAI Chat Completions wire format: OpenAI, OpenRouter, Groq, Mistral, DeepSeek, Together, Ollama /v1, LM Studio, KoboldCpp, vLLM...</summary>
    public sealed class OpenAICompatibleProvider : HttpChatProvider
    {
        public OpenAICompatibleProvider(ProviderOptions o, HttpClient http = null) : base(o, http) { }

        public override string Name => "OpenAI-compatible";

        protected override string Validate()
        {
            if (string.IsNullOrWhiteSpace(Options.Endpoint)) return "no endpoint URL set.";
            if (!Uri.TryCreate(Options.Endpoint, UriKind.Absolute, out _)) return "endpoint is not a valid URL.";
            return base.Validate();
        }

        protected override string AdaptiveFlag(string message)
        {
            string m = message.ToLowerInvariant();
            if (m.Contains("temperature")) return "no_temperature";
            if (m.Contains("max_tokens") || m.Contains("max_completion_tokens")) return "max_completion_tokens";
            return null;
        }

        protected override Attempt Build(ChatRequest req, HashSet<string> flags)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(req.SystemPrompt))
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", req.SystemPrompt } });
            foreach (var m in req.Messages)
            {
                if (m.Role == ChatRole.System) continue;
                messages.Add(new Dictionary<string, object> { { "role", m.Role == ChatRole.User ? "user" : "assistant" }, { "content", m.Content } });
            }
            var body = new Dictionary<string, object>
            {
                { "model", Options.Model },
                { "messages", messages },
            };
            body[flags.Contains("max_completion_tokens") ? "max_completion_tokens" : "max_tokens"] = (long)req.MaxTokens;
            if (req.Temperature >= 0 && !flags.Contains("no_temperature")) body["temperature"] = (double)req.Temperature;

            var a = new Attempt { Url = JoinUrl(Options.Endpoint, "/chat/completions"), Body = J.Stringify(body) };
            if (!string.IsNullOrEmpty(Options.ApiKey))
            {
                if (string.Equals(Options.AuthHeader, "Authorization", StringComparison.OrdinalIgnoreCase)) a.Headers["Authorization"] = "Bearer " + Options.ApiKey;
                else a.Headers[Options.AuthHeader] = Options.ApiKey;
            }
            if (Options.Endpoint.IndexOf("openrouter.ai", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                a.Headers["X-Title"] = "GTA V Organization Empire";
            }
            return a;
        }

        protected override ChatResult Parse(object json)
        {
            var choices = J.Get(json, "choices") as List<object>;
            if (choices == null || choices.Count == 0)
            {
                string err = J.GetString(J.Get(json, "error"), "message");
                return ChatResult.Fail(err != null ? AiErrorKind.BadRequest : AiErrorKind.MalformedResponse, err ?? "no choices in response");
            }
            var choice = choices[0];
            string finish = J.GetString(choice, "finish_reason");
            var message = J.Get(choice, "message");
            object content = J.Get(message, "content");
            string text = null;
            if (content is string s) text = s;
            else if (content is List<object> parts)
            {
                var sb = new StringBuilder();
                foreach (var p in parts) sb.Append(J.GetString(p, "text") ?? "");
                text = sb.ToString();
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                string refusal = J.GetString(message, "refusal");
                if (refusal != null || finish == "content_filter") return ChatResult.Fail(AiErrorKind.Refused, refusal ?? "content filtered");
                if (finish == "length") return ChatResult.Fail(AiErrorKind.MalformedResponse, "reply was cut off - raise MaxResponseTokens");
                return ChatResult.Fail(AiErrorKind.MalformedResponse, "empty reply");
            }
            return ChatResult.Ok(text, J.GetString(json, "model"));
        }
    }

    /// <summary>Anthropic Messages API (raw HTTP; keeps the mod free of extra assemblies in the GTA scripts folder).</summary>
    public sealed class AnthropicProvider : HttpChatProvider
    {
        public const string ApiVersion = "2026-10-21";
        public const string FallbackBeta = "server-side-fallback-2026-07-01";
        public const string DefaultModel = "claude-opus-5-5";

        public AnthropicProvider(ProviderOptions o, HttpClient http = null) : base(o, http) { }

        public override string Name => "Anthropic";

        protected override string Validate()
        {
            if (string.IsNullOrWhiteSpace(Options.ApiKey)) return "no API key set.";
            return base.Validate();
        }

        protected override string AdaptiveFlag(string message)
        {
            string m = message.ToLowerInvariant();
            if (m.Contains("fallback")) return "no_fallbacks";
            if (m.Contains("output_config") || m.Contains("effort")) return "no_effort";
            return null;
        }

        protected override Attempt Build(ChatRequest req, HashSet<string> flags)
        {
            var messages = new List<object>();
            foreach (var m in NormalizeTurns(req.Messages))
            {
                messages.Add(new Dictionary<string, object> { { "role", m.Role == ChatRole.User ? "user" : "assistant" }, { "content", m.Content } });
            }
            // Current models may think before answering and thinking counts toward max_tokens,
            // so leave headroom; reply length itself is constrained by the system prompt.
            long maxTokens = Math.Min(8192, req.MaxTokens + 2048);
            var body = new Dictionary<string, object>
            {
                { "model", Options.Model },
                { "max_tokens", maxTokens },
                { "messages", messages },
            };
            if (!string.IsNullOrEmpty(req.SystemPrompt)) body["system"] = req.SystemPrompt;
            if (!flags.Contains("no_effort") && !string.IsNullOrEmpty(Options.AnthropicEffort))
                body["output_config"] = new Dictionary<string, object> { { "effort", Options.AnthropicEffort } };
            bool fallbacks = Options.AnthropicFallbacks && !flags.Contains("no_fallbacks");
            if (fallbacks) body["fallbacks"] = "default";
            // Note: temperature is intentionally never sent (rejected by API version 2026-10-21).

            string baseUrl = string.IsNullOrWhiteSpace(Options.Endpoint) ? "https://api.anthropic.com" : Options.Endpoint;
            string url = baseUrl.TrimEnd('/');
            if (!url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
                url = url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url + "/messages" : url + "/v1/messages";
            var a = new Attempt { Url = url, Body = J.Stringify(body) };
            a.Headers["x-api-key"] = Options.ApiKey;
            a.Headers["anthropic-version"] = ApiVersion;
            if (fallbacks) a.Headers["anthropic-beta"] = FallbackBeta;
            return a;
        }

        protected override ChatResult Parse(object json)
        {
            string stop = J.GetString(json, "stop_reason");
            var content = J.Get(json, "content") as List<object>;
            var sb = new StringBuilder();
            if (content != null)
            {
                foreach (var block in content)
                {
                    if (J.GetString(block, "type") == "text") sb.Append(J.GetString(block, "text") ?? "");
                }
            }
            if (stop == "refusal") return ChatResult.Fail(AiErrorKind.Refused, J.GetString(J.Get(json, "stop_details"), "explanation") ?? "declined");
            string text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                if (stop == "max_tokens") return ChatResult.Fail(AiErrorKind.MalformedResponse, "reply was cut off - raise MaxResponseTokens");
                return ChatResult.Fail(AiErrorKind.MalformedResponse, "empty reply");
            }
            return ChatResult.Ok(text, J.GetString(json, "model"));
        }
    }

    /// <summary>Google Gemini generateContent API.</summary>
    public sealed class GeminiProvider : HttpChatProvider
    {
        public GeminiProvider(ProviderOptions o, HttpClient http = null) : base(o, http) { }

        public override string Name => "Gemini";

        protected override string Validate()
        {
            if (string.IsNullOrWhiteSpace(Options.ApiKey)) return "no API key set.";
            return base.Validate();
        }

        protected override string AdaptiveFlag(string message)
        {
            return message.ToLowerInvariant().Contains("temperature") ? "no_temperature" : null;
        }

        protected override Attempt Build(ChatRequest req, HashSet<string> flags)
        {
            var contents = new List<object>();
            foreach (var m in NormalizeTurns(req.Messages))
            {
                contents.Add(new Dictionary<string, object>
                {
                    { "role", m.Role == ChatRole.User ? "user" : "model" },
                    { "parts", new List<object> { new Dictionary<string, object> { { "text", m.Content } } } },
                });
            }
            var gen = new Dictionary<string, object> { { "maxOutputTokens", (long)Math.Min(8192, req.MaxTokens + 1024) } };
            if (req.Temperature >= 0 && !flags.Contains("no_temperature")) gen["temperature"] = (double)req.Temperature;
            var body = new Dictionary<string, object> { { "contents", contents }, { "generationConfig", gen } };
            if (!string.IsNullOrEmpty(req.SystemPrompt))
                body["systemInstruction"] = new Dictionary<string, object> { { "parts", new List<object> { new Dictionary<string, object> { { "text", req.SystemPrompt } } } } };
            string baseUrl = string.IsNullOrWhiteSpace(Options.Endpoint) ? "https://generativelanguage.googleapis.com/v1beta" : Options.Endpoint.TrimEnd('/');
            var a = new Attempt
            {
                Url = baseUrl + "/models/" + Uri.EscapeDataString(Options.Model) + ":generateContent",
                Body = J.Stringify(body),
            };
            a.Headers["x-goog-api-key"] = Options.ApiKey;
            return a;
        }

        protected override ChatResult Parse(object json)
        {
            string block = J.GetString(J.Get(json, "promptFeedback"), "blockReason");
            if (block != null) return ChatResult.Fail(AiErrorKind.Refused, block);
            var candidates = J.Get(json, "candidates") as List<object>;
            if (candidates == null || candidates.Count == 0) return ChatResult.Fail(AiErrorKind.MalformedResponse, "no candidates");
            var cand = candidates[0];
            string finish = J.GetString(cand, "finishReason");
            var parts = J.Get(J.Get(cand, "content"), "parts") as List<object>;
            var sb = new StringBuilder();
            if (parts != null)
            {
                foreach (var p in parts)
                {
                    if (J.Get(p, "thought") is bool t && t) continue;
                    sb.Append(J.GetString(p, "text") ?? "");
                }
            }
            if (sb.Length == 0)
            {
                if (finish == "SAFETY" || finish == "PROHIBITED_CONTENT") return ChatResult.Fail(AiErrorKind.Refused, finish);
                if (finish == "MAX_TOKENS") return ChatResult.Fail(AiErrorKind.MalformedResponse, "reply was cut off - raise MaxResponseTokens");
                return ChatResult.Fail(AiErrorKind.MalformedResponse, "empty reply");
            }
            return ChatResult.Ok(sb.ToString(), J.GetString(json, "modelVersion"));
        }
    }

    public static class ProviderFactory
    {
        public static IChatProvider Create(AiSettings settings, string resolvedKey, HttpClient http = null)
        {
            if (!settings.Enabled) return new OfflineDialogueProvider();
            var opt = ProviderOptions.From(settings, resolvedKey);
            switch (settings.Provider)
            {
                case AiProviderKind.OpenAICompatible: return new OpenAICompatibleProvider(opt, http);
                case AiProviderKind.Anthropic: return new AnthropicProvider(opt, http);
                case AiProviderKind.Gemini: return new GeminiProvider(opt, http);
                default: return new OfflineDialogueProvider();
            }
        }
    }
}
