// node --test app/src/lib/money.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatJD, normalizeJoMobile } from './money.ts';

test('BUG-004 any pasted Jordanian mobile keeps all 9 digits', () => {
  for (const s of ['+962 79 123 4567', '00962791234567', '0791234567', '79 123 4567', '7a9😀1 234 567', '+962-79-123-4567', '791234567999'])
    assert.equal(normalizeJoMobile(s), '79 123 4567', s);
  assert.equal(normalizeJoMobile(''), '');
  assert.equal(normalizeJoMobile('79'), '79');
});

test('amounts keep the sign with the digits', () => {
  assert.equal(formatJD(-3000).replace(/[⁦⁩]/g, ''), '−3.000 د.ا');
  assert.equal(formatJD(5000, 'ar', true).replace(/[⁦⁩]/g, ''), '+5.000 د.ا');
});
