import test from "node:test";
import assert from "node:assert/strict";
import { CHUNK_CHARS, MemoryBackend, Storage } from "../RealisticWorld_BP/scripts/core/storage.js";

const make = () => {
  const backend = new MemoryBackend();
  return { backend, s: new Storage(backend, "rw:") };
};

test("round trip of a small value", () => {
  const { s } = make();
  assert.equal(s.save("a", { x: 1, y: [true, "z"] }).ok, true);
  const r = s.load("a");
  assert.equal(r.ok, true);
  assert.deepEqual(r.value, { x: 1, y: [true, "z"] });
  assert.equal(r.recovered, false);
});

test("missing key reports missing, not corrupt", () => {
  const { s } = make();
  const r = s.load("nothing");
  assert.equal(r.ok, false);
  assert.equal(r.missing, true);
});

test("large values are split into chunks under the game's string limit", () => {
  const { s, backend } = make();
  const big = { t: "é".repeat(CHUNK_CHARS * 2 + 50) }; // non-ASCII on purpose
  const res = s.save("big", big);
  assert.equal(res.ok, true);
  assert.equal(res.chunks, 3);
  for (const [, v] of backend.map) {
    if (typeof v === "string") assert.ok(Buffer.byteLength(v, "utf8") <= 32767, "chunk over 32767 bytes");
  }
  assert.deepEqual(s.load("big").value, big);
});

test("saves alternate slots and keep the previous copy", () => {
  const { s, backend } = make();
  s.save("k", { v: 1 });
  s.save("k", { v: 2 });
  assert.match(String(backend.get("rw:k@a")), /^RW1\|1\|/);
  assert.match(String(backend.get("rw:k@b")), /^RW1\|2\|/);
  assert.equal(s.load("k").value.v, 2);
  s.save("k", { v: 3 }); // overwrites slot a (older)
  assert.match(String(backend.get("rw:k@a")), /^RW1\|3\|/);
  assert.equal(s.load("k").value.v, 3);
});

test("corrupted newest copy falls back to the previous copy", () => {
  const { s, backend } = make();
  s.save("k", { v: 1 });
  s.save("k", { v: 2 });
  backend.set("rw:k@b#0", '{"v":7}'); // same length, different content -> checksum mismatch
  const r = s.load("k");
  assert.equal(r.ok, true);
  assert.equal(r.value.v, 1);
  assert.equal(r.recovered, true);
  assert.equal(s.stats.recoveries, 1);
});

test("missing chunk (crash while saving) falls back", () => {
  const { s, backend } = make();
  s.save("k", { t: "a".repeat(CHUNK_CHARS + 10) });
  s.save("k", { t: "b".repeat(CHUNK_CHARS + 10) });
  backend.set("rw:k@b#1", undefined);
  const r = s.load("k");
  assert.equal(r.ok, true);
  assert.equal(r.value.t[0], "a");
});

test("both copies corrupt -> clear error, no exception", () => {
  const { s, backend } = make();
  s.save("k", { v: 1 });
  s.save("k", { v: 2 });
  backend.set("rw:k@a#0", "garbage");
  backend.set("rw:k@b#0", "garbage");
  const r = s.load("k");
  assert.equal(r.ok, false);
  assert.equal(r.missing, false);
  assert.match(r.error, /corrupt/);
});

test("garbage header is ignored", () => {
  const { s, backend } = make();
  s.save("k", { v: 1 });
  backend.set("rw:k@b", "RW1|999|nonsense");
  assert.equal(s.load("k").value.v, 1);
});

test("shrinking data leaves no stale chunks; delete removes everything", () => {
  const { s, backend } = make();
  s.save("k", { t: "x".repeat(CHUNK_CHARS * 3) });
  s.save("k", { t: "x".repeat(CHUNK_CHARS * 3) });
  s.save("k", { t: "small" }); // slot a shrinks from 4 chunks to 1
  assert.equal(backend.get("rw:k@a#1"), undefined);
  assert.equal(s.load("k").value.t, "small");
  s.delete("k");
  assert.deepEqual(backend.ids(), []);
});

test("unserialisable value is rejected without touching saved data", () => {
  const { s } = make();
  s.save("k", { v: 1 });
  const cyclic = {};
  cyclic.self = cyclic;
  const res = s.save("k", cyclic);
  assert.equal(res.ok, false);
  assert.equal(s.load("k").value.v, 1);
});
