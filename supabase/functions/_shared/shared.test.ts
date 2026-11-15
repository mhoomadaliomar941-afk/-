// node --test supabase/functions/_shared/shared.test.ts   (Node 22+ strips the types)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createCipheriv, randomBytes } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { classifyResult, createCheckout, decryptWebhook, formatAmount, parseWebhook } from './hyperpay.ts';
import { roundAfter, roundTime } from './drand.ts';
import { chunk } from './push.ts';
import { parseTopup } from './validate.ts';
import { winningSpot } from '../../../app/src/data/fairness.ts';

const sql = (q: string) => execFileSync('su', ['postgres', '-c', `psql -qtAX -d replica_backend_test -c "${q}"`]).toString().trim();

test('result codes', () => {
  assert.equal(classifyResult('000.000.000'), 'paid');
  assert.equal(classifyResult('000.100.110'), 'paid');
  assert.equal(classifyResult('000.200.000'), 'pending');
  assert.equal(classifyResult('800.100.151'), 'failed');
  assert.equal(classifyResult(''), 'failed');
});

test('amount format is 3-decimal JOD from integer fils', () => {
  assert.equal(formatAmount(5000), '5.000');
  assert.equal(formatAmount(1500), '1.500');
  assert.throws(() => formatAmount(0));
  assert.throws(() => formatAmount(1.5));
});

test('webhook decrypts with our key and rejects tampering', async () => {
  const key = randomBytes(32), iv = randomBytes(12);
  const evt = { type: 'PAYMENT', payload: { merchantTransactionId: 'pay-1', ndc: 'chk-1', result: { code: '000.000.000' } } };
  const c = createCipheriv('aes-256-gcm', key, iv);
  const body = Buffer.concat([c.update(JSON.stringify(evt)), c.final()]);
  const tag = c.getAuthTag();
  const out = await decryptWebhook(key.toString('hex'), iv.toString('hex'), tag.toString('hex'), body.toString('hex'));
  assert.deepEqual(out, evt);
  assert.deepEqual(parseWebhook(out), { paymentId: 'pay-1', checkoutId: 'chk-1', outcome: 'paid' });
  body[0] ^= 1;
  await assert.rejects(decryptWebhook(key.toString('hex'), iv.toString('hex'), tag.toString('hex'), body.toString('hex')));
  await assert.rejects(decryptWebhook(randomBytes(32).toString('hex'), iv.toString('hex'), tag.toString('hex'), body.toString('hex')));
  assert.equal(parseWebhook({ type: 'REGISTRATION', payload: {} }), null);
});

test('checkout request carries our payment id and amount, token in header only', async () => {
  let seen: { url: string; init: RequestInit } | null = null;
  const fake = (async (url: string, init: RequestInit) => { seen = { url, init }; return new Response(JSON.stringify({ id: 'ABC123.uat01-vm-tx04' })); }) as unknown as typeof fetch;
  const r = await createCheckout({ baseUrl: 'https://test.example', accessToken: 'tok', entityId: 'ent' }, { amountFils: 5000, paymentId: 'pay-9' }, fake);
  assert.equal(r.checkoutId, 'ABC123.uat01-vm-tx04');
  const body = new URLSearchParams(String(seen!.init.body));
  assert.equal(body.get('amount'), '5.000'); assert.equal(body.get('currency'), 'JOD');
  assert.equal(body.get('merchantTransactionId'), 'pay-9'); assert.equal(body.get('accessToken'), null);
  assert.equal((seen!.init.headers as Record<string, string>).Authorization, 'Bearer tok');
});

test('drand round matches the database function', () => {
  for (const iso of ['2026-11-15T10:00:00Z', '2026-11-15T10:00:29.999Z', '2027-01-01T00:00:00Z']) {
    assert.equal(roundAfter(new Date(iso)), Number(sql(`select drand_round_after('${iso}')`)), iso);
    assert.ok(roundTime(roundAfter(new Date(iso))) > new Date(iso), 'round is after the fill');
  }
});

test('app and database pick the same winning spot', () => {
  for (let i = 0; i < 25; i++) {
    const seed = randomBytes(32).toString('hex'), beacon = randomBytes(32).toString('hex'), total = 1 + Math.floor(Math.random() * 1000);
    const db = Number(sql(`select ((('x' || substr(encode(digest('${seed}' || '${beacon}', 'sha256'), 'hex'), 1, 15))::bit(60)::bigint % ${total}) + 1)`));
    assert.equal(winningSpot(seed, beacon, total), db);
  }
});

test('top-up input validation', () => {
  assert.deepEqual(parseTopup({ amountFils: 5000, method: 'card' }), { amountFils: 5000, method: 'card' });
  for (const bad of [{ amountFils: 500, method: 'card' }, { amountFils: 1500, method: 'card' }, { amountFils: 501000, method: 'card' }, { amountFils: '5000', method: 'card' }, { amountFils: 5000, method: 'cash' }, null]) {
    assert.throws(() => parseTopup(bad));
  }
});

test('push batches of 100', () => {
  assert.deepEqual(chunk(Array.from({ length: 250 }, (_, i) => i)).map((c) => c.length), [100, 100, 50]);
});
