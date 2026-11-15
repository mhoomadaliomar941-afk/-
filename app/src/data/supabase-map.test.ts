// node --test app/src/data/supabase-map.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { errorCode, toAssignment, toNotification, toProduct } from './supabase-map.ts';

test('every exception raised by the SQL functions maps to a code the screens handle', () => {
  const cases: [string, string][] = [
    ['only 2 spots left', 'sold_out'], ['product is not open', 'not_open'], ['product closed', 'not_open'],
    ['limit is 5 spots per member', 'limit'], ['insufficient balance', 'insufficient'], ['insufficient shopping balance', 'insufficient'],
    ['not signed in', 'not_signed_in'], ['product not found', 'not_found'], ['balance_not_zero', 'balance_not_zero'],
    ['active_participation', 'active_participation'], ['open_order', 'open_order'], ['Token has expired or is invalid', 'wrong_otp'],
    ['spots must be at least 1', 'invalid'], ['amount must be positive', 'invalid'], ['Failed to fetch', 'network'], ['weird', 'unknown'],
  ];
  for (const [m, code] of cases) assert.equal(errorCode(m), code, m);
});

test('product row maps money as numbers (bigint arrives as string)', () => {
  const p = toProduct({ id: 'p', title_ar: 'هاتف', category: 'phone', retail_price_fils: '899000', spot_price_fils: '1000', total_spots: 50, filled_spots: 3, status: 'open', seed_hash: 'ab' });
  assert.equal(p.retailPriceFils, 899000); assert.equal(p.spotPriceFils, 1000); assert.deepEqual(p.specs, {}); assert.equal(p.maxSpotsPerUser, null);
});

test('assignment never exposes who won, only whether it is me', () => {
  assert.equal(toAssignment({ is_mine: true, beacon_round: '5' }, 'u1').ownerId, 'u1');
  assert.equal(toAssignment({ is_mine: false, beacon_round: '5' }, 'u1').ownerId, 'someone-else');
  assert.equal(toAssignment({ is_mine: true, beacon_round: '5' }, 'u1').beaconRound, 5);
});

test('notification text is the app\'s, with the product title when known', () => {
  const n = toNotification({ id: 'n', type: 'credited', payload: { product_id: 'p1' }, created_at: 'x' }, { p1: 'هاتف' });
  assert.equal(n.title, 'رجعلك رصيد تسوق'); assert.equal(n.body, 'هاتف'); assert.equal(n.productId, 'p1'); assert.equal(n.readAt, null);
});
