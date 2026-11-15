// In-memory data layer with seed data. Same function signatures as the real
// Supabase layer (see types.ts `Api`), and the same rules as the SQL
// functions in replica/schema.sql, so swapping it changes no screen code.
import { sha256 } from 'js-sha256';
import { seedHash, winningSpot } from './fairness';
import {
  Address, Api, ApiError, Assignment, Notification, Order, Participation, PaymentMethod, Product,
  Profile, Redemption, ShowcaseItem, Store, Wallet, WalletTxn, StoreProduct, StoreOrder, AdminOrder,
} from './types';

const DEV_OTP = '123456';
const LATENCY = 250;
const ME = 'u-me';
/** Demo: these numbers log in as the owner/admin (the test number and the owner's own); any other number is a regular member. */
export const ADMIN_PHONES = ['+962790000000', '+962786055772'];

let n = 0;
const id = (p: string) => `${p}-${Date.now().toString(36)}-${(n++).toString(36)}`;
const now = () => new Date().toISOString();
const ago = (min: number) => new Date(Date.now() - min * 60_000).toISOString();
const wait = <T,>(v: T) => new Promise<T>((r) => setTimeout(() => r(v), LATENCY));

// ---------------------------------------------------------------- seed

type ProductRow = Product & { serverSeed: string };
function product(p: Omit<ProductRow, 'seedHash' | 'serverSeed' | 'images'> & { images?: string[] }): ProductRow {
  const serverSeed = sha256(`seed-${p.id}`);
  return { images: [], ...p, serverSeed, seedHash: seedHash(serverSeed) };
}

const db = {
  session: null as Profile | null,
  profiles: new Map<string, Profile>([[ME, { id: ME, phone: '', fullName: null, isAdmin: false }]]),
  storeProducts: [] as StoreProduct[],
  storeOrders: [] as (StoreOrder & { userId: string; addressId: string; idempotencyKey: string })[],
  products: [
    product({ id: 'p-phone', titleAr: 'هاتف ذكي برو ٢٥٦ جيجا', brand: null, category: 'phone',
      descriptionAr: 'هاتف رائد بشاشة ٦٫٧ إنش وكاميرا ثلاثية. جديد بالكرتونة مع كفالة الوكيل.',
      specs: { 'الذاكرة': '256GB', 'الشاشة': '6.7"', 'اللون': 'تيتانيوم' },
      retailPriceFils: 899_000, spotPriceFils: 1_000, totalSpots: 50, filledSpots: 47, maxSpotsPerUser: 5,
      status: 'open', closesAt: null }),
    product({ id: 'p-watch', titleAr: 'ساعة ذكية الإصدار العاشر', brand: null, category: 'watch',
      descriptionAr: 'ساعة ذكية مقاومة للماء مع تتبّع النوم ونبض القلب.',
      specs: { 'الحجم': '46mm', 'البطارية': 'يومين' },
      retailPriceFils: 280_000, spotPriceFils: 1_000, totalSpots: 400, filledSpots: 352, maxSpotsPerUser: 10,
      status: 'open', closesAt: null }),
    product({ id: 'p-console', titleAr: 'جهاز ألعاب الجيل الخامس مع يدّين', brand: null, category: 'console',
      descriptionAr: 'جهاز ألعاب منزلي بدقة 4K، مع يد تحكم إضافية.',
      specs: { 'التخزين': '1TB', 'الدقة': '4K' },
      retailPriceFils: 420_000, spotPriceFils: 2_000, totalSpots: 300, filledSpots: 120, maxSpotsPerUser: 10,
      status: 'open', closesAt: null }),
    product({ id: 'p-audio', titleAr: 'سماعات لاسلكية بعزل الضوضاء', brand: null, category: 'audio',
      descriptionAr: 'سماعات داخل الأذن مع عزل نشط للضوضاء وعلبة شحن لاسلكي.',
      specs: { 'البطارية': '30 ساعة مع العلبة' },
      retailPriceFils: 150_000, spotPriceFils: 1_000, totalSpots: 200, filledSpots: 31, maxSpotsPerUser: 5,
      status: 'open', closesAt: null }),
    product({ id: 'p-laptop', titleAr: 'لابتوب نحيف ١٤ إنش', brand: null, category: 'laptop',
      descriptionAr: 'لابتوب خفيف للعمل والدراسة.', specs: { 'المعالج': '10 أنوية', 'الذاكرة': '16GB' },
      retailPriceFils: 1_100_000, spotPriceFils: 5_000, totalSpots: 300, filledSpots: 300, maxSpotsPerUser: 10,
      status: 'assigned', closesAt: null }),
  ] as ProductRow[],
  wallets: new Map<string, Wallet>([[ME, { cashFils: 4_000, shoppingFils: 0 }]]),
  txns: [
    { id: 't-seed', bucket: 'cash', type: 'topup', amountFils: 4_000, status: 'done', createdAt: ago(60 * 26) },
  ] as WalletTxn[],
  participations: [] as Participation[],
  spots: new Map<string, string[]>(), // productId -> userId per spot number (index = number - 1)
  assignments: [] as Assignment[],
  addresses: [] as Address[],
  orders: [] as Order[],
  redemptions: [] as Redemption[],
  notifications: [
    { id: 'n-seed', type: 'product_open', title: 'منتج جديد', body: 'نزلت سماعات لاسلكية، الأماكن من دينار.', productId: 'p-audio', readAt: null, createdAt: ago(90) },
  ] as Notification[],
  showcase: [
    { id: 's1', displayName: 'ليث ع.', productTitle: 'لابتوب نحيف ١٤ إنش', category: 'laptop', date: ago(60 * 24 * 2) },
    { id: 's2', displayName: 'رهف م.', productTitle: 'ساعة ذكية الإصدار التاسع', category: 'watch', date: ago(60 * 24 * 6) },
    { id: 's3', displayName: 'يزن ق.', productTitle: 'جهاز ألعاب الجيل الخامس', category: 'console', date: ago(60 * 24 * 11) },
  ] as ShowcaseItem[],
  stores: [
    { id: 'st1', nameAr: 'سوبرماركت الحيّ', governorate: 'عمّان', city: 'عمّان', category: 'بقالة' },
    { id: 'st2', nameAr: 'مكتبة القلم', governorate: 'عمّان', city: 'عمّان', category: 'قرطاسية' },
    { id: 'st3', nameAr: 'إلكترونيات الشمال', governorate: 'إربد', city: 'إربد', category: 'إلكترونيات' },
    { id: 'st4', nameAr: 'مخبز الزرقاء', governorate: 'الزرقاء', city: 'الزرقاء', category: 'مخابز' },
    { id: 'st5', nameAr: 'أزياء العقبة', governorate: 'العقبة', city: 'العقبة', category: 'ملابس' },
  ] as Store[],
  idem: new Map<string, Participation>(),
};

// other members' spots, so products look busy
for (const p of db.products) {
  db.spots.set(p.id, Array.from({ length: p.filledSpots }, (_, i) => `u-other-${i % 37}`));
}
// the past laptop result
{
  const p = db.products.find((x) => x.id === 'p-laptop')!;
  const beaconValue = sha256('beacon-laptop');
  const win = winningSpot(p.serverSeed, beaconValue, p.totalSpots);
  db.assignments.push({ id: 'a-laptop', productId: p.id, winningSpot: win, ownerId: 'u-other-3', ownerName: 'ليث ع.',
    serverSeed: p.serverSeed, beaconRound: 4_812_337, beaconValue, assignedAt: ago(60 * 24 * 2) });
  // this member held one spot (not the winning one) and was credited 5 JD shopping balance
  const holders = db.spots.get(p.id)!;
  holders[win - 1] = 'u-other-3';
  const mine = win === 1 ? 2 : 1;
  holders[mine - 1] = ME;
  db.participations.push({ id: 'pa-laptop', userId: ME, productId: p.id, spots: 1, amountFils: 5_000, status: 'credited',
    createdAt: ago(60 * 24 * 6), spotNumbers: [mine] });
  wallet(ME).shoppingFils += 5_000;
  db.txns.push({ id: 't-laptop-credit', bucket: 'shopping', type: 'credit', amountFils: 5_000, status: 'done', label: p.titleAr, createdAt: ago(60 * 24 * 2) });
  db.txns.push({ id: 't-laptop-spend', bucket: 'cash', type: 'spend', amountFils: -5_000, status: 'done', label: p.titleAr, createdAt: ago(60 * 24 * 6) });
  db.txns.push({ id: 't-seed1', bucket: 'cash', type: 'topup', amountFils: 5_000, status: 'done', createdAt: ago(60 * 24 * 7) });
}

// a tablet this member already won, waiting for a delivery address (shows S10 and the "won" tab)
{
  const p = product({ id: 'p-tablet', titleAr: 'تابلت ١١ إنش للرسم والدراسة', brand: null, category: 'tablet',
    descriptionAr: 'تابلت بشاشة ١١ إنش مع قلم.', specs: { 'الذاكرة': '128GB' },
    retailPriceFils: 390_000, spotPriceFils: 1_000, totalSpots: 120, filledSpots: 120, maxSpotsPerUser: 5,
    status: 'assigned', closesAt: null });
  db.products.push(p);
  const holders = Array.from({ length: 120 }, (_, i) => `u-other-${i % 37}`);
  // Demo seed: the beacon is fixed first, then this member is given the spot that won (plus the one after it).
  const round = 4_700_101;
  const beaconValue = sha256(`beacon-${round}`);
  const win = winningSpot(p.serverSeed, beaconValue, 120);
  const mine = [win, win === 120 ? 119 : win + 1].sort((a, b) => a - b);
  mine.forEach((n) => { holders[n - 1] = ME; });
  db.spots.set(p.id, holders);
  db.participations.push({ id: 'pa-tablet', userId: ME, productId: p.id, spots: 2, amountFils: 2_000, status: 'owner',
    createdAt: ago(60 * 24 * 4), spotNumbers: mine });
  db.assignments.push({ id: 'a-tablet', productId: p.id, winningSpot: win, ownerId: ME, ownerName: 'أنت', serverSeed: p.serverSeed,
    beaconRound: round, beaconValue, assignedAt: ago(60 * 5) });
  db.orders.push({ id: 'o-tablet', assignmentId: 'a-tablet', productId: p.id, addressId: null, status: 'awaiting_address' });
  db.notifications.unshift({ id: 'n-tablet', type: 'owner', title: 'مبروك! ربحت المنتج', body: p.titleAr, productId: p.id, readAt: null, createdAt: ago(60 * 5) });
  db.txns.push({ id: 't-tablet', bucket: 'cash', type: 'spend', amountFils: -2_000, status: 'done', label: p.titleAr, createdAt: ago(60 * 24 * 4) });
  db.txns.push({ id: 't-seed0', bucket: 'cash', type: 'topup', amountFils: 2_000, status: 'done', createdAt: ago(60 * 24 * 5) });
}

// the owner's store
db.storeProducts.push(
  { id: 'sp-charger', titleAr: 'شاحن سريع ٣٠ واط', descriptionAr: 'شاحن جداري USB-C مع كيبل.', category: 'other', priceFils: 9_000, stock: 25, images: [], active: true },
  { id: 'sp-case', titleAr: 'كفر جوال شفاف', descriptionAr: 'كفر سيليكون شفاف بحماية للزوايا.', category: 'phone', priceFils: 4_500, stock: 40, images: [], active: true },
  { id: 'sp-earbuds', titleAr: 'سماعات أذن سلكية', descriptionAr: 'سماعات بمايك وزر تحكم.', category: 'audio', priceFils: 6_000, stock: 3, images: [], active: true },
  { id: 'sp-mug', titleAr: 'مج حراري', descriptionAr: 'بيحافظ على حرارة المشروب ٦ ساعات.', category: 'home', priceFils: 7_500, stock: 0, images: [], active: true },
  { id: 'sp-hidden', titleAr: 'منتج مخفي (تجربة)', descriptionAr: '', category: 'other', priceFils: 1_000, stock: 5, images: [], active: false },
);

// ---------------------------------------------------------------- store events

const listeners = new Set<() => void>();
// Call counts, read by the e2e tests (window.__fakeCalls) to catch request loops.
const calls = { getRedemption: 0 };
(globalThis as { __fakeCalls?: typeof calls }).__fakeCalls = calls;
const emit = () => listeners.forEach((l) => l());

function requireMe(): string {
  if (!db.session) throw new ApiError('not_signed_in');
  return db.session.id;
}
function requireAdmin() {
  requireMe();
  if (!db.session!.isAdmin) throw new ApiError('forbidden');
}
function wallet(uid: string) {
  if (!db.wallets.has(uid)) db.wallets.set(uid, { cashFils: 0, shoppingFils: 0 });
  return db.wallets.get(uid)!;
}
function publicProduct(p: ProductRow): Product {
  const { serverSeed: _hidden, ...rest } = p;
  return { ...rest };
}
function findProduct(pid: string) {
  const p = db.products.find((x) => x.id === pid);
  if (!p) throw new ApiError('not_found');
  return p;
}

// The "assign-full-products" job: waits for the next public beacon round, then picks.
function scheduleAssignment(pid: string) {
  setTimeout(() => {
    const p = findProduct(pid);
    if (p.status !== 'full') return;
    const beaconRound = 4_900_000 + Math.floor(Math.random() * 1000);
    const beaconValue = sha256(`beacon-${beaconRound}-${Date.now()}`);
    const win = winningSpot(p.serverSeed, beaconValue, p.totalSpots);
    const ownerId = db.spots.get(pid)![win - 1];
    const a: Assignment = { id: id('a'), productId: pid, winningSpot: win, ownerId,
      ownerName: ownerId === ME ? 'أنت' : 'عضو آخر', serverSeed: p.serverSeed, beaconRound, beaconValue, assignedAt: now() };
    db.assignments.push(a);
    p.status = 'assigned';
    db.orders.push({ id: id('o'), assignmentId: a.id, productId: pid, addressId: null, status: 'awaiting_address' });
    for (const part of db.participations.filter((x) => x.productId === pid)) {
      if (part.userId === ownerId) { part.status = 'owner'; continue; }
      part.status = 'credited';
      wallet(part.userId).shoppingFils += part.amountFils;
      if (part.userId === ME) {
        db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'credit', amountFils: part.amountFils, status: 'done', label: p.titleAr, createdAt: now() });
      }
    }
    const mine = db.participations.some((x) => x.productId === pid && x.userId === ME);
    if (mine) {
      const won = ownerId === ME;
      db.notifications.unshift({ id: id('n'), type: won ? 'owner' : 'credited', productId: pid, readAt: null, createdAt: now(),
        title: won ? 'مبروك! ربحت المنتج' : 'رجعلك رصيد تسوق', body: p.titleAr });
    }
    emit();
  }, 2500);
}

// The "expire-redemptions" job: like the real cron, once a minute, not when a code is read.
function expireRedemptionsJob() {
  let changed = false;
  for (const r of db.redemptions) {
    if (r.status !== 'pending' || new Date(r.expiresAt) > new Date()) continue;
    r.status = 'expired';
    wallet(ME).shoppingFils += r.amountFils;
    db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'refund', amountFils: r.amountFils, status: 'done', createdAt: now() });
    changed = true;
  }
  if (changed) emit(); // BUG-006: screens showing the balance reload
}
setInterval(expireRedemptionsJob, 60_000);

// ---------------------------------------------------------------- the API

export const fakeApi: Api = {
  async sendOtp(phone) {
    if (!/^\+9627[789]\d{7}$/.test(phone)) throw new ApiError('invalid');
    await wait(null);
  },
  async verifyOtp(phone, code) {
    await wait(null);
    if (code !== DEV_OTP) throw new ApiError('wrong_otp');
    const p = db.profiles.get(ME)!;
    p.phone = phone;
    p.isAdmin = ADMIN_PHONES.includes(phone);
    db.session = p;
    emit();
    return p;
  },
  async me() { return db.session; },
  async signOut() { db.session = null; emit(); },

  async listProducts() {
    const order = { open: 0, full: 1, assigned: 2, delivered: 3, cancelled: 4, draft: 5 } as const;
    return wait(db.products.filter((p) => p.status !== 'draft').map(publicProduct)
      .sort((a, b) => order[a.status] - order[b.status] || b.filledSpots / b.totalSpots - a.filledSpots / a.totalSpots));
  },
  async getProduct(pid) { return wait(publicProduct(findProduct(pid))); },
  async mySpotsIn(pid) {
    if (!db.session) return 0;
    return db.participations.filter((x) => x.productId === pid && x.userId === ME).reduce((s, x) => s + x.spots, 0);
  },

  // buy_spots(): idempotent, never oversells, debits cash, marks full.
  async buySpots(pid, spots, key) {
    const uid = requireMe();
    await wait(null);
    const prior = db.idem.get(key);
    if (prior) return prior;
    if (!Number.isInteger(spots) || spots < 1) throw new ApiError('invalid');
    const p = findProduct(pid);
    if (p.status !== 'open') throw new ApiError('not_open');
    if (p.filledSpots + spots > p.totalSpots) throw new ApiError('sold_out', String(p.totalSpots - p.filledSpots));
    const mine = await fakeApi.mySpotsIn(pid);
    if (p.maxSpotsPerUser && mine + spots > p.maxSpotsPerUser) throw new ApiError('limit');
    const cost = p.spotPriceFils * spots;
    const w = wallet(uid);
    if (w.cashFils < cost) throw new ApiError('insufficient');

    w.cashFils -= cost;
    const holders = db.spots.get(pid)!;
    const first = p.filledSpots + 1;
    for (let i = 0; i < spots; i++) holders.push(uid);
    p.filledSpots += spots;
    const part: Participation = { id: id('pa'), userId: uid, productId: pid, spots, amountFils: cost, status: 'active',
      createdAt: now(), spotNumbers: Array.from({ length: spots }, (_, i) => first + i) };
    db.participations.unshift(part);
    db.idem.set(key, part);
    db.txns.unshift({ id: id('t'), bucket: 'cash', type: 'spend', amountFils: -cost, status: 'done', label: p.titleAr, createdAt: now() });
    if (p.filledSpots === p.totalSpots) { p.status = 'full'; scheduleAssignment(pid); }
    emit();
    return part;
  },

  // Fake gateway: pending for a moment, then the "webhook" confirms.
  async startTopup(amountFils, _method: PaymentMethod) {
    const uid = requireMe();
    if (!Number.isInteger(amountFils) || amountFils < 1_000) throw new ApiError('invalid');
    const txn: WalletTxn = { id: id('t'), bucket: 'cash', type: 'topup', amountFils, status: 'pending', createdAt: now() };
    db.txns.unshift(txn);
    emit();
    await wait(null);
    txn.status = 'done';
    wallet(uid).cashFils += amountFils;
    emit();
    return { paymentId: txn.id };
  },
  async getWallet() { return wait({ ...wallet(requireMe()) }); },
  async listTxns() { requireMe(); return wait([...db.txns]); },
  async listParticipations() {
    const uid = requireMe();
    return wait(db.participations.filter((x) => x.userId === uid).map((x) => ({ ...x, product: publicProduct(findProduct(x.productId)) })));
  },
  async getAssignment(pid) { return wait(db.assignments.find((a) => a.productId === pid) ?? null); },

  async listAddresses() { requireMe(); return wait([...db.addresses]); },
  async addAddress(a) {
    requireMe();
    if (!a.governorate || !a.city || !a.street || !/^\+?\d{9,13}$/.test(a.phone.replace(/\s/g, ''))) throw new ApiError('invalid');
    const row = { ...a, id: id('ad') };
    db.addresses.push(row); emit();
    return wait(row);
  },
  async getOrderForProduct(pid) {
    const uid = requireMe();
    const a = db.assignments.find((x) => x.productId === pid && x.ownerId === uid);
    return wait(a ? db.orders.find((o) => o.assignmentId === a.id) ?? null : null);
  },
  async setOrderAddress(oid, aid) {
    requireMe();
    const o = db.orders.find((x) => x.id === oid);
    if (!o) throw new ApiError('not_found');
    if (!db.addresses.some((x) => x.id === aid)) throw new ApiError('not_found');
    o.addressId = aid; o.status = 'pending'; emit();
    return wait({ ...o });
  },

  async listShowcase() { return wait([...db.showcase]); },
  async listStores() { return wait([...db.stores]); },
  async getStore(sid) {
    const s = db.stores.find((x) => x.id === sid);
    if (!s) throw new ApiError('not_found');
    return wait(s);
  },
  // create_redemption(): holds shopping balance, 15-minute code.
  async createRedemption(sid, amountFils) {
    const uid = requireMe();
    await wait(null);
    if (!Number.isInteger(amountFils) || amountFils <= 0) throw new ApiError('invalid');
    const w = wallet(uid);
    if (w.shoppingFils < amountFils) throw new ApiError('insufficient');
    w.shoppingFils -= amountFils;
    const r: Redemption = { id: id('r'), storeId: sid, amountFils, status: 'pending',
      code: sha256(id('code')).slice(0, 10).toUpperCase(), expiresAt: new Date(Date.now() + 15 * 60_000).toISOString() };
    db.redemptions.unshift(r);
    const store = db.stores.find((s) => s.id === sid);
    db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'redeem', amountFils: -amountFils, status: 'done', label: store?.nameAr, createdAt: now() });
    emit();
    return r;
  },
  async getRedemption(rid) {
    calls.getRedemption++;
    const r = db.redemptions.find((x) => x.id === rid);
    if (!r) throw new ApiError('not_found');
    return wait({ ...r });
  },

  async listNotifications() { requireMe(); return wait([...db.notifications]); },
  async markNotificationsRead() { db.notifications.forEach((x) => { x.readAt ??= now(); }); emit(); },

  async registerPushToken() { /* nothing to deliver in the fake layer */ },
  // request_account_deletion(): same preconditions as the SQL function
  async deleteAccount() {
    const uid = requireMe();
    await wait(null);
    const w = wallet(uid);
    if (w.cashFils > 0 || w.shoppingFils > 0) throw new ApiError('balance_not_zero');
    if (db.participations.some((x) => x.userId === uid && x.status === 'active')) throw new ApiError('active_participation');
    if (db.orders.some((o) => ['awaiting_address', 'pending', 'shipped'].includes(o.status) && db.assignments.find((a) => a.id === o.assignmentId)?.ownerId === uid)) throw new ApiError('open_order');
    db.session = null; emit();
  },
  // ---------------------------------------------------------------- the owner's store
  async listStoreProducts() { return wait(db.storeProducts.filter((p) => p.active).map((p) => ({ ...p }))); },
  async getStoreProduct(pid) {
    const p = db.storeProducts.find((x) => x.id === pid && (x.active || db.session?.isAdmin));
    if (!p) throw new ApiError('not_found');
    return wait({ ...p });
  },
  // checkout_store(): shopping balance first, then cash; stock locked; idempotent
  async checkoutStore(items, addressId, key) {
    const uid = requireMe();
    await wait(null);
    const prior = db.storeOrders.find((o) => o.idempotencyKey === key);
    if (prior) return prior;
    if (!items.length) throw new ApiError('invalid');
    if (!db.addresses.some((a) => a.id === addressId)) throw new ApiError('not_found');
    let total = 0;
    for (const it of items) {
      const p = db.storeProducts.find((x) => x.id === it.productId);
      if (!p || !p.active) throw new ApiError('not_found');
      if (!Number.isInteger(it.qty) || it.qty < 1 || it.qty > 20) throw new ApiError('invalid');
      if (p.stock < it.qty) throw new ApiError('out_of_stock', String(p.stock));
      total += p.priceFils * it.qty;
    }
    const w = wallet(uid);
    if (w.shoppingFils + w.cashFils < total) throw new ApiError('insufficient');
    const fromShopping = Math.min(w.shoppingFils, total);
    w.shoppingFils -= fromShopping; w.cashFils -= total - fromShopping;
    const o = { id: id('so'), userId: uid, addressId, idempotencyKey: key, totalFils: total, paidShoppingFils: fromShopping,
      paidCashFils: total - fromShopping, status: 'pending' as const, createdAt: now(),
      items: items.map((it) => { const p = db.storeProducts.find((x) => x.id === it.productId)!; p.stock -= it.qty;
        return { productId: p.id, titleAr: p.titleAr, qty: it.qty, unitPriceFils: p.priceFils }; }) };
    db.storeOrders.unshift(o);
    if (fromShopping) db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'purchase', amountFils: -fromShopping, status: 'done', label: 'المتجر', createdAt: now() });
    if (total - fromShopping) db.txns.unshift({ id: id('t'), bucket: 'cash', type: 'purchase', amountFils: -(total - fromShopping), status: 'done', label: 'المتجر', createdAt: now() });
    emit();
    return o;
  },
  async listMyStoreOrders() { const uid = requireMe(); return wait(db.storeOrders.filter((o) => o.userId === uid).map((o) => ({ ...o }))); },

  // ---------------------------------------------------------------- admin
  async adminListProducts() { requireAdmin(); return wait(db.products.map(publicProduct)); },
  async adminSaveProduct(input) {
    requireAdmin();
    await wait(null);
    if (!input.titleAr.trim() || input.retailPriceFils <= 0 || input.spotPriceFils < 1000 || input.totalSpots < 2) throw new ApiError('invalid');
    if (input.id) {
      const p = findProduct(input.id);
      if (p.status !== 'draft') throw new ApiError('invalid', 'only drafts can be edited');
      Object.assign(p, { titleAr: input.titleAr, descriptionAr: input.descriptionAr, category: input.category, retailPriceFils: input.retailPriceFils,
        spotPriceFils: input.spotPriceFils, totalSpots: input.totalSpots, maxSpotsPerUser: input.maxSpotsPerUser, images: input.images });
      emit(); return publicProduct(p);
    }
    const p: ProductRow = { id: id('p'), titleAr: input.titleAr, brand: null, category: input.category, descriptionAr: input.descriptionAr, specs: {},
      retailPriceFils: input.retailPriceFils, spotPriceFils: input.spotPriceFils, totalSpots: input.totalSpots, filledSpots: 0,
      maxSpotsPerUser: input.maxSpotsPerUser, status: 'draft', closesAt: null, seedHash: '', serverSeed: '', images: input.images };
    db.products.unshift(p); db.spots.set(p.id, []);
    emit(); return publicProduct(p);
  },
  // open_product(): secret seed made here, only its hash is published
  async adminOpenProduct(pid) {
    requireAdmin();
    const p = findProduct(pid);
    if (p.status !== 'draft') throw new ApiError('invalid');
    p.serverSeed = sha256(`seed-${pid}-${Date.now()}-${Math.random()}`);
    p.seedHash = seedHash(p.serverSeed);
    p.status = 'open';
    db.notifications.unshift({ id: id('n'), type: 'product_open', title: 'منتج جديد', body: p.titleAr, productId: p.id, readAt: null, createdAt: now() });
    emit(); return wait(publicProduct(p));
  },
  // cancel_product(): everyone refunded in cash
  async adminCancelProduct(pid) {
    requireAdmin();
    const p = findProduct(pid);
    if (p.status !== 'draft' && p.status !== 'open') throw new ApiError('invalid');
    p.status = 'cancelled';
    for (const part of db.participations.filter((x) => x.productId === pid)) {
      part.status = 'refunded';
      wallet(part.userId).cashFils += part.amountFils;
      if (part.userId === ME) db.txns.unshift({ id: id('t'), bucket: 'cash', type: 'refund', amountFils: part.amountFils, status: 'done', label: p.titleAr, createdAt: now() });
    }
    emit(); await wait(null);
  },
  async adminListStoreProducts() { requireAdmin(); return wait(db.storeProducts.map((p) => ({ ...p }))); },
  async adminSaveStoreProduct(input) {
    requireAdmin();
    await wait(null);
    if (!input.titleAr.trim() || input.priceFils <= 0 || !Number.isInteger(input.stock) || input.stock < 0) throw new ApiError('invalid');
    if (input.id) {
      const p = db.storeProducts.find((x) => x.id === input.id);
      if (!p) throw new ApiError('not_found');
      Object.assign(p, { ...input, id: p.id });
      emit(); return { ...p };
    }
    const p: StoreProduct = { ...input, id: id('sp') };
    db.storeProducts.unshift(p);
    emit(); return { ...p };
  },
  async adminListOrders() {
    requireAdmin();
    const addr = (aid: string | null) => { const a = db.addresses.find((x) => x.id === aid); return a ? `${a.governorate}، ${a.city}، ${a.street}` : null; };
    const phone = (uid: string) => (uid === ME ? db.profiles.get(ME)!.phone : '+962 7X XXX XXXX');
    const draw: AdminOrder[] = db.orders.map((o) => {
      const a = db.assignments.find((x) => x.id === o.assignmentId)!;
      return { kind: 'draw', id: o.id, title: findProduct(o.productId).titleAr, status: o.status, createdAt: a.assignedAt,
        customer: { phone: phone(a.ownerId), address: addr(o.addressId) } };
    });
    const store: AdminOrder[] = db.storeOrders.map((o) => ({ kind: 'store', id: o.id, title: o.items.map((i) => `${i.titleAr} × ${i.qty}`).join('، '),
      status: o.status, createdAt: o.createdAt, totalFils: o.totalFils, customer: { phone: phone(o.userId), address: addr(o.addressId) } }));
    return wait([...draw, ...store].sort((x, y) => y.createdAt.localeCompare(x.createdAt)));
  },
  async adminSetOrderStatus(kind, oid, status) {
    requireAdmin();
    await wait(null);
    if (kind === 'draw') {
      const o = db.orders.find((x) => x.id === oid);
      if (!o) throw new ApiError('not_found');
      if (!o.addressId) throw new ApiError('invalid', 'no address yet');
      o.status = status as Order['status'];
      const a = db.assignments.find((x) => x.id === o.assignmentId)!;
      if (status === 'shipped' && a.ownerId === ME) db.notifications.unshift({ id: id('n'), type: 'order_shipped', title: 'طلبك بالطريق', body: findProduct(o.productId).titleAr, productId: o.productId, readAt: null, createdAt: now() });
    } else {
      const o = db.storeOrders.find((x) => x.id === oid);
      if (!o) throw new ApiError('not_found');
      if (o.status === 'cancelled') throw new ApiError('invalid');
      if (status === 'cancelled') {
        if (o.status === 'delivered') throw new ApiError('invalid');
        o.items.forEach((i) => { const p = db.storeProducts.find((x) => x.id === i.productId); if (p) p.stock += i.qty; });
        const w = wallet(o.userId); w.shoppingFils += o.paidShoppingFils; w.cashFils += o.paidCashFils;
        if (o.userId === ME) {
          if (o.paidShoppingFils) db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'refund', amountFils: o.paidShoppingFils, status: 'done', label: 'المتجر', createdAt: now() });
          if (o.paidCashFils) db.txns.unshift({ id: id('t'), bucket: 'cash', type: 'refund', amountFils: o.paidCashFils, status: 'done', label: 'المتجر', createdAt: now() });
        }
      }
      o.status = status as StoreOrder['status'];
    }
    emit();
  },
  async uploadProductImage(uri) { requireAdmin(); return uri; }, // the fake keeps the local photo

  subscribe(l) { listeners.add(l); return () => listeners.delete(l); },
};
