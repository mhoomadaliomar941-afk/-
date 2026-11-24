# AI Dialogue Setup

Members (and, optionally, any eligible NPC) can hold free-form conversations with you. Open a conversation by looking
at someone and pressing **T**, from the interaction popup (**E** > Talk), or from **F7 > AI Dialogue** (members far
away answer over the radio).

## Providers

| Provider type | Works with | Endpoint example | Key |
|---|---|---|---|
| `Offline` (default) | Built-in dialogue engine | - | none |
| `OpenAICompatible` | OpenAI, OpenRouter, Groq, Mistral, DeepSeek, Together, Azure OpenAI (`AuthHeader = api-key`), **Ollama** (`http://localhost:11434/v1`), **LM Studio** (`http://localhost:1234/v1`), KoboldCpp, vLLM... | `https://api.openai.com/v1` | usually |
| `Anthropic` | Claude models via the Messages API | `https://api.anthropic.com` | yes |
| `Gemini` | Google Gemini `generateContent` | `https://generativelanguage.googleapis.com/v1beta` | yes |

In-game: **F7 > Settings > AI dialogue > Apply a provider preset**, then set **Model** (exactly as your provider names
it) and the **API key**, then **Test connection**. The test runs in the background; the game never waits for it.

Notes:
- The Anthropic preset defaults to `claude-opus-5-5`. For faster and cheaper chatter you can enter
  `claude-haiku-5-5` instead. Anthropic requests use API version `2026-10-21`, a short-dialogue effort level (`low` by
  default) and, optionally, server-side fallbacks (Settings > AI). Temperature is never sent to Anthropic.
- Some models reject `temperature` or `max_tokens`; the OpenAI-compatible adapter automatically retries without the
  rejected parameter. Set Temperature to `-1` to never send it.
- Local servers (Ollama, LM Studio) are free and private; no key is needed.

## API key storage and security

Choose one (Settings > AI dialogue > API key):

1. **Plain text** in `OrganizationEmpire.ini` (`ApiKey = ...`). Simple, but anyone with access to your files can read it.
2. **Environment variable**: `ApiKey = env:OPENAI_API_KEY` - the key never touches the ini.
3. **Encrypted with Windows DPAPI**: *Encrypt stored key* rewrites it as `dpapi:...`, decryptable only by your Windows
   user account on this PC.

Keys are never written to the log: every log line passes through a redactor that masks registered secrets and
common key formats. **A key stored on your own PC can never be guaranteed fully secure** - use a key with a spending
limit, and revoke it if you share your files.

## What is sent to the provider

Each request contains: a system prompt with the NPC's generated profile (name, rank, role, personality, loyalty and
morale bands, relationships, current orders), a short faction status summary, a few recent faction events, a limited
number of the NPC's memories, the last *MemoryTurns* exchanges, and the message you typed. Nothing else from your PC
is sent. Use the Offline or a local provider if you prefer that nothing leaves your machine.

## Cost and rate limits

- One request per message you send; nothing is sent in the background.
- *MinSecondsBetweenRequests* (default 1.5 s) and a limit of two simultaneous requests prevent spamming.
- *MaxResponseTokens*, *MaxReplyWords*, *MemoryTurns* and *MemoriesPerRequest* bound the size of each exchange.
- On HTTP 429 the mod waits briefly and retries once.

## Memory

Each member keeps long-term memories (joined the faction, promotions/demotions, orders, missions, how conversations
made them feel) and recent dialogue. Limits: *MaxMemoryEntries* per NPC; overflow is folded into a short summary
instead of growing forever; civilian memories are capped at 30 NPCs. Clear memories per member (member file) or
globally (AI Dialogue page / Settings). With *PersistentMemory* off, memories are not stored in saves.

## Safety model: the AI can't act

The model only produces text. The mod strips formatting codes and the optional tags it understands
(`[[MOOD:+1]]`, `[[MOOD:-1]]`, `[[WILLING]]`), whose effects are small and capped (mood: at most 3 adjustments per
member per day; willingness: at most +20% on the *next* recruitment roll - it never recruits anyone by itself).

If **your** message contains an order ("follow me", "stay here", "get in the car"...), a chip appears:
*Order detected: Follow me - [Ctrl+Enter] or click to execute*. Only when you confirm does the mod's command system
run it, with the same validation as the menu. The model's reply is never parsed into commands.

The system prompt keeps characters inside the GTA fiction and instructs the model not to give real-world operational
details about weapons, drugs, evading police or attacks.

## Errors and fallback

If a request fails (bad key, no credit, wrong model, timeout, network, refusal), the chat shows the reason and the
offline engine answers instead so the conversation continues. See the log for details (keys are redacted).

| Message | Typical cause |
|---|---|
| *The API key was rejected* | Wrong key, wrong provider type, or no credit |
| *Request rejected: HTTP 404* | Model name or endpoint path wrong |
| *Could not reach the AI provider* | No internet, firewall, local server not running |
| *The AI provider timed out* | Slow model; raise TimeoutSeconds |
| *Reply was cut off* | Raise MaxResponseTokens (reasoning models need more) |
