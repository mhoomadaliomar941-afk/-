// In-memory data layer with seed data. Same function signatures as the real
// Supabase layer (see types.ts `Api`), and the same rules as the SQL
// functions in replica/schema.sql, so swapping it changes no screen code.
import { sha256 } from 'js-sha256';
import { seedHash, winningSpot } from './fairness';
import {
  Address, Api, ApiError, Assignment, Notification, Order, Participation, PaymentMethod, Product,
  Profile, Redemption, ShowcaseItem, Store, Wallet, WalletTxn,
} from './types';

const DEV_OTP = '123456';
const LATENCY = 250;
const ME = 'u-me';

let n = 0;
const id = (p: string) => `${p}-${Date.now().toString(36)}-${(n++).toString(36)}`;
const now = () => new Date().toISOString();
const ago = (min: number) => new Date(Date.now() - min * 60_000).toISOString();
const wait = <T,>(v: T) => new Promise<T>((r) => setTimeout(() => r(v), LATENCY));

// ---------------------------------------------------------------- seed

type ProductRow = Product & { serverSeed: string };
function product(p: Omit<ProductRow, 'seedHash' | 'serverSeed'>): ProductRow {
  const serverSeed = sha256(`seed-${p.id}`);
  return { ...p, serverSeed, seedHash: seedHash(serverSeed) };
}

const db = {
  session: null as Profile | null,
  profiles: new Map<string, Profile>([[ME, { id: ME, phone: '', fullName: null }]]),
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

// ---------------------------------------------------------------- store events

const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

function requireMe(): string {
  if (!db.session) throw new ApiError('not_signed_in');
  return db.session.id;
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
    const r = db.redemptions.find((x) => x.id === rid);
    if (!r) throw new ApiError('not_found');
    if (r.status === 'pending' && new Date(r.expiresAt) <= new Date()) {
      r.status = 'expired';
      wallet(requireMe()).shoppingFils += r.amountFils; // expire_redemptions(): give it back
      db.txns.unshift({ id: id('t'), bucket: 'shopping', type: 'refund', amountFils: r.amountFils, status: 'done', createdAt: now() });
    }
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
  subscribe(l) { listeners.add(l); return () => listeners.delete(l); },
};
