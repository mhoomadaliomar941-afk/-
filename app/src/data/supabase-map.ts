// Pure mapping between database rows (snake_case, replica/schema.sql) and app types.
// No imports beyond types, so it can be unit-tested with plain Node.
import type {
  Address, ApiError as ApiErrorT, Assignment, Category, Notification, Order, Participation, Product, Redemption,
  ShowcaseItem, Store, WalletTxn,
} from './types';

type Row = Record<string, any>;

export const toProduct = (r: Row): Product => ({
  id: r.id, titleAr: r.title_ar, brand: r.brand ?? null, category: r.category as Category, descriptionAr: r.description_ar ?? '',
  specs: r.specs ?? {}, retailPriceFils: Number(r.retail_price_fils), spotPriceFils: Number(r.spot_price_fils),
  totalSpots: r.total_spots, filledSpots: r.filled_spots, maxSpotsPerUser: r.max_spots_per_user ?? null,
  status: r.status, closesAt: r.closes_at ?? null, seedHash: r.seed_hash ?? '',
});

export const toParticipation = (r: Row): Participation => ({
  id: r.id, userId: r.user_id, productId: r.product_id, spots: r.spots, amountFils: Number(r.amount_fils),
  status: r.status, createdAt: r.created_at, spotNumbers: r.spot_numbers ?? [],
});

export const toAssignment = (r: Row, me: string | null): Assignment => ({
  id: r.id, productId: r.product_id, winningSpot: r.winning_spot,
  ownerId: r.is_mine && me ? me : 'someone-else', ownerName: r.is_mine ? 'أنت' : 'عضو آخر',
  serverSeed: r.server_seed, beaconRound: Number(r.beacon_round), beaconValue: r.beacon_value, assignedAt: r.assigned_at,
});

export const toTxn = (r: Row): WalletTxn => ({
  id: r.id, bucket: r.bucket, type: r.type, amountFils: Number(r.amount_fils), status: 'done', createdAt: r.created_at,
});

/** A pending top-up has no ledger row yet; show it from the payments table. */
export const pendingPaymentToTxn = (r: Row): WalletTxn => ({
  id: `pay-${r.id}`, bucket: 'cash', type: 'topup', amountFils: Number(r.amount_fils),
  status: r.status === 'failed' ? 'failed' : 'pending', createdAt: r.created_at,
});

export const toAddress = (r: Row): Address => ({
  id: r.id, governorate: r.governorate, city: r.city, area: r.area ?? '', street: r.street ?? '', phone: r.phone,
});
export const fromAddress = (a: Omit<Address, 'id'>, userId: string) => ({
  user_id: userId, governorate: a.governorate.trim(), city: a.city.trim(), area: a.area.trim() || null, street: a.street.trim(), phone: a.phone.trim(),
});

export const toOrder = (r: Row, productId: string): Order => ({
  id: r.id, assignmentId: r.assignment_id, productId, addressId: r.address_id ?? null, status: r.status,
});

export const toStore = (r: Row): Store => ({ id: r.id, nameAr: r.name_ar, governorate: r.governorate, city: r.city, category: r.category ?? '' });

export const toRedemption = (r: Row): Redemption => ({
  id: r.id, storeId: r.store_id, amountFils: Number(r.amount_fils), code: r.code, status: r.status, expiresAt: r.expires_at,
});

export const toShowcase = (r: Row): ShowcaseItem => ({
  id: r.id, displayName: r.display_name, productTitle: r.product_title, category: r.category, date: r.published_at,
});

const NOTE_TEXT: Record<Notification['type'], { title: string; body: string }> = {
  owner: { title: 'مبروك! ربحت المنتج', body: 'أدخل عنوان التوصيل.' },
  credited: { title: 'رجعلك رصيد تسوق', body: 'استخدمه بأي متجر شريك.' },
  product_open: { title: 'منتج جديد', body: 'نزل منتج جديد، الأماكن محدودة.' },
  order_shipped: { title: 'طلبك بالطريق', body: 'المنتج انشحن لعنوانك.' },
};
export const toNotification = (r: Row, productTitles: Record<string, string> = {}): Notification => {
  const productId = r.payload?.product_id as string | undefined;
  const text = NOTE_TEXT[r.type as Notification['type']] ?? { title: 'سبوت', body: '' };
  return { id: r.id, type: r.type, title: text.title, body: (productId && productTitles[productId]) || text.body, productId, readAt: r.read_at ?? null, createdAt: r.created_at };
};

/** Database exceptions (raise exception '...') -> the app's error codes. */
export function errorCode(message: string | undefined | null): ApiErrorT['code'] {
  const m = message ?? '';
  if (/only \d+ spots left/.test(m)) return 'sold_out';
  if (/product is not open|product closed/.test(m)) return 'not_open';
  if (/limit is \d+ spots/.test(m)) return 'limit';
  if (/insufficient (shopping )?balance/.test(m)) return 'insufficient';
  if (/not signed in|JWT|401/.test(m)) return 'not_signed_in';
  if (/not found/.test(m)) return 'not_found';
  if (/balance_not_zero/.test(m)) return 'balance_not_zero';
  if (/active_participation/.test(m)) return 'active_participation';
  if (/open_order/.test(m)) return 'open_order';
  if (/Token has expired or is invalid|otp/i.test(m)) return 'wrong_otp';
  if (/rate limit|too many|429/i.test(m)) return 'rate_limited';
  if (/Network request failed|Failed to fetch/i.test(m)) return 'network';
  if (/must be|invalid|check constraint|violates/.test(m)) return 'invalid';
  return 'unknown';
}
