#!/usr/bin/env python3
"""Send a prompt to a free LLM API: try Groq first, fall back to OpenRouter.

Keys come from environment variables (never hardcode them):
    GROQ_API_KEY        https://console.groq.com/keys
    OPENROUTER_API_KEY  https://openrouter.ai/keys

In a Claude cloud session where the keys are saved as network secrets, the
proxy injects them, so any placeholder value works:
    GROQ_API_KEY=proxy-injected OPENROUTER_API_KEY=proxy-injected python3 llm_chat.py "hi"

Usage:
    python3 llm_chat.py "your question"
    python3 llm_chat.py            # interactive chat, type "exit" to quit
"""

import json
import os
import sys
import urllib.error
import urllib.request

PROVIDERS = [
    {
        "name": "Groq",
        "url": "https://api.groq.com/openai/v1/chat/completions",
        "key_env": "GROQ_API_KEY",
        "models": ["openai/gpt-oss-120b", "qwen/qwen3.8-27b", "openai/gpt-oss-20b"],
    },
    {
        "name": "OpenRouter",
        "url": "https://openrouter.ai/api/v1/chat/completions",
        "key_env": "OPENROUTER_API_KEY",
        # openrouter/free picks any free model that is available right now.
        "models": ["openrouter/free", "google/gemma-4-31b-it:free"],
    },
]

TIMEOUT = 90
MAX_TOKENS = 2000  # reasoning models need room to think before they answer


class ProviderError(Exception):
    pass


def call(provider, model, messages):
    key = os.environ.get(provider["key_env"])
    if not key:
        raise ProviderError(f"{provider['key_env']} is not set")
    body = json.dumps(
        {"model": model, "messages": messages, "max_tokens": MAX_TOKENS}
    ).encode()
    req = urllib.request.Request(
        provider["url"],
        data=body,
        headers={
            "Authorization": f"Bearer {key}",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
            data = json.load(resp)
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:200]
        raise ProviderError(f"HTTP {e.code}: {detail}") from None
    except (urllib.error.URLError, TimeoutError) as e:
        raise ProviderError(f"network error: {e}") from None

    try:
        content = data["choices"][0]["message"].get("content")
    except (KeyError, IndexError, TypeError):
        raise ProviderError(f"unexpected response: {str(data)[:200]}") from None
    if not content or not content.strip():
        raise ProviderError("empty answer")
    return content.strip(), data.get("model", model)


def ask(messages):
    """Return (answer, 'Provider/model'), trying each provider and model in order."""
    errors = []
    for provider in PROVIDERS:
        for model in provider["models"]:
            try:
                answer, used = call(provider, model, messages)
                return answer, f"{provider['name']}/{used}"
            except ProviderError as e:
                errors.append(f"  {provider['name']} {model}: {e}")
                if "is not set" in str(e) or "HTTP 401" in str(e):
                    break  # no or bad key: other models of this provider fail too
    raise SystemExit("All providers failed:\n" + "\n".join(errors))


def main():
    if len(sys.argv) > 1:
        answer, used = ask([{"role": "user", "content": " ".join(sys.argv[1:])}])
        print(answer)
        print(f"\n[{used}]", file=sys.stderr)
        return

    history = []
    print('Chat started. Type "exit" to quit.')
    while True:
        try:
            text = input("\nYou: ").strip()
        except (EOFError, KeyboardInterrupt):
            break
        if text.lower() in {"exit", "quit", "خروج"}:
            break
        if not text:
            continue
        history.append({"role": "user", "content": text})
        answer, used = ask(history)
        history.append({"role": "assistant", "content": answer})
        print(f"\nAI [{used}]: {answer}")


if __name__ == "__main__":
    main()
