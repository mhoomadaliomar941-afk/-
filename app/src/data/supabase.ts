// The real data layer: Supabase (Postgres + RLS + RPC) behind the same `Api` the
// screens already use. Writes that move money are database functions; the app
// never updates a balance itself.
import 'react-native-url-polyfill/auto';
import AsyncStorage from '@react-native-async-storage/async-storage';
import * as WebBrowser from 'expo-web-browser';
import { Platform } from 'react-native';
import { createClient, PostgrestError } from '@supabase/supabase-js';
import {
  addressLine, errorCode, fromAddress, fromDrawProduct, fromStoreProduct, toStoreOrder, toStoreProduct, pendingPaymentToTxn, toAddress, toAssignment, toNotification, toOrder, toParticipation, toProduct,
  toRedemption, toShowcase, toStore, toTxn,
} from './supabase-map';
import { Api, ApiError, Profile } from './types';

export function createSupabaseApi(url: string, anonKey: string): Api {
  const sb = createClient(url, anonKey, {
    auth: {
      storage: Platform.OS === 'web' ? undefined : AsyncStorage,
      autoRefreshToken: true, persistSession: true, detectSessionInUrl: false,
    },
  });

  const listeners = new Set<() => void>();
  const emit = () => listeners.forEach((l) => l());
  let channel: ReturnType<typeof sb.channel> | null = null;

  type Row = Record<string, any>;
  type Res = { data: unknown; error: PostgrestError | null };
  /** Each throws ApiError for any Supabase error. `check` may return null (maybeSingle),
   *  `list` always returns an array, `one` insists on a row. */
  function check(res: Res): Row | null {
    if (res.error) throw new ApiError(errorCode(res.error.message), res.error.message);
    return (res.data as Row | null) ?? null;
  }
  function list(res: Res): Row[] {
    if (res.error) throw new ApiError(errorCode(res.error.message), res.error.message);
    return (res.data as Row[] | null) ?? [];
  }
  function one(res: Res): Row {
    const d = check(res);
    if (d == null) throw new ApiError('not_found');
    return d;
  }
  async function uid(): Promise<string> {
    const { data } = await sb.auth.getSession();
    const id = data.session?.user.id;
    if (!id) throw new ApiError('not_signed_in');
    return id;
  }
  async function productTitles(ids: string[]) {
    if (!ids.length) return {};
    const rows = list(await sb.from('products').select('id, title_ar').in('id', ids));
    return Object.fromEntries((rows ?? []).map((r) => [r.id, r.title_ar]));
  }

  // Live changes (RLS-filtered by Supabase): balance, results, notifications, product progress.
  function listen(userId: string | null) {
    if (channel) { sb.removeChannel(channel); channel = null; }
    if (!userId) return;
    channel = sb.channel(`member-${userId}`)
      .on('postgres_changes', { event: '*', schema: 'public', table: 'wallets', filter: `user_id=eq.${userId}` }, emit)
      .on('postgres_changes', { event: '*', schema: 'public', table: 'notifications', filter: `user_id=eq.${userId}` }, emit)
      .on('postgres_changes', { event: '*', schema: 'public', table: 'orders', filter: `user_id=eq.${userId}` }, emit)
      .on('postgres_changes', { event: 'UPDATE', schema: 'public', table: 'products' }, emit)
      .subscribe();
  }
  sb.auth.onAuthStateChange((_e, session) => { listen(session?.user.id ?? null); emit(); });

  const api: Api = {
    async sendOtp(phone) {
      if (!/^\+9627[789]\d{7}$/.test(phone)) throw new ApiError('invalid');
      const { error } = await sb.auth.signInWithOtp({ phone });
      if (error) throw new ApiError(errorCode(error.message), error.message);
    },
    async verifyOtp(phone, token) {
      const { error } = await sb.auth.verifyOtp({ phone, token, type: 'sms' });
      if (error) throw new ApiError('wrong_otp', error.message);
      const me = await api.me();
      if (!me) throw new ApiError('not_signed_in');
      return me;
    },
    async me(): Promise<Profile | null> {
      const { data } = await sb.auth.getSession();
      if (!data.session) return null;
      const row = check(await sb.from('profiles').select('id, phone, full_name, is_admin').eq('id', data.session.user.id).maybeSingle());
      return row ? { id: row.id, phone: row.phone, fullName: row.full_name, isAdmin: !!row.is_admin } : null;
    },
    async signOut() { await sb.auth.signOut(); },

    async listProducts() {
      const rows = list(await sb.from('products').select('*').neq('status', 'draft').neq('status', 'cancelled'));
      const order = { open: 0, full: 1, assigned: 2, delivered: 3, cancelled: 4, draft: 5 } as const;
      return rows.map(toProduct).sort((a, b) => order[a.status] - order[b.status] || b.filledSpots / b.totalSpots - a.filledSpots / a.totalSpots);
    },
    async getProduct(id) {
      const row = check(await sb.from('products').select('*').eq('id', id).maybeSingle());
      if (!row) throw new ApiError('not_found');
      return toProduct(row);
    },
    async mySpotsIn(productId) {
      const { data } = await sb.auth.getSession();
      if (!data.session) return 0;
      const rows = list(await sb.from('participations').select('spots').eq('product_id', productId));
      return rows.reduce((s, r) => s + r.spots, 0);
    },
    async buySpots(productId, spots, key) {
      const row = one(await sb.rpc('buy_spots', { p_product: productId, p_spots: spots, p_key: key }).single());
      emit();
      return toParticipation({ ...row, spot_numbers: [] });
    },

    // Opens HyperPay's own page in an in-app browser; money arrives by webhook.
    async startTopup(amountFils, method) {
      const { data, error } = await sb.functions.invoke('topup-start', { body: { amountFils, method } });
      if (error || !data?.payUrl) throw new ApiError(errorCode(error?.message ?? data?.error), error?.message);
      await WebBrowser.openAuthSessionAsync(data.payUrl, 'nazih://topup');
      emit();
      return { paymentId: data.paymentId };
    },
    async getWallet() {
      const row = check(await sb.from('wallets').select('cash_fils, shopping_fils').maybeSingle());
      return { cashFils: Number(row?.cash_fils ?? 0), shoppingFils: Number(row?.shopping_fils ?? 0) };
    },
    async listTxns() {
      await uid();
      const [txns, pending] = await Promise.all([
        sb.from('wallet_txns').select('*').order('created_at', { ascending: false }).limit(100),
        sb.from('payments').select('id, amount_fils, status, created_at').in('status', ['pending', 'failed'])
          .gte('created_at', new Date(Date.now() - 7 * 86_400_000).toISOString()),
      ]);
      return [...list(pending).map(pendingPaymentToTxn), ...list(txns).map(toTxn)]
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    },
    async listParticipations() {
      await uid();
      const parts = list(await sb.from('my_participations').select('*').order('created_at', { ascending: false }));
      const ids = [...new Set(parts.map((p) => p.product_id as string))];
      const products = ids.length ? list(await sb.from('products').select('*').in('id', ids)).map(toProduct) : [];
      const byId = Object.fromEntries(products.map((p) => [p.id, p]));
      return parts.map((r) => ({ ...toParticipation(r), product: byId[r.product_id] })).filter((x) => x.product);
    },
    async getAssignment(productId) {
      const { data } = await sb.auth.getSession();
      if (!data.session) return null;
      const row = check(await sb.from('public_assignments').select('*').eq('product_id', productId).maybeSingle());
      return row ? toAssignment(row, data.session.user.id) : null;
    },

    async listAddresses() { await uid(); return list(await sb.from('addresses').select('*').order('created_at')).map(toAddress); },
    async addAddress(a) {
      const me = await uid();
      if (!a.governorate.trim() || !a.city.trim() || !a.street.trim() || !/^\+?\d{9,13}$/.test(a.phone.replace(/\s/g, ''))) throw new ApiError('invalid');
      return toAddress(one(await sb.from('addresses').insert(fromAddress(a, me)).select('*').single()));
    },
    async getOrderForProduct(productId) {
      await uid();
      const a = check(await sb.from('public_assignments').select('id, is_mine').eq('product_id', productId).maybeSingle());
      if (!a?.is_mine) return null;
      const o = check(await sb.from('orders').select('*').eq('assignment_id', a.id).maybeSingle());
      return o ? toOrder(o, productId) : null;
    },
    async setOrderAddress(orderId, addressId) {
      const row = one(await sb.rpc('set_order_address', { p_order: orderId, p_address: addressId }).single());
      const a = one(await sb.from('public_assignments').select('product_id').eq('id', row.assignment_id as string).single());
      emit();
      return toOrder(row, a.product_id);
    },

    async listShowcase() { return list(await sb.from('public_showcase').select('*').order('published_at', { ascending: false }).limit(50)).map(toShowcase); },
    async listStores() { return list(await sb.from('partner_stores').select('*').order('governorate')).map(toStore); },
    async getStore(id) {
      const row = check(await sb.from('partner_stores').select('*').eq('id', id).maybeSingle());
      if (!row) throw new ApiError('not_found');
      return toStore(row);
    },
    async createRedemption(storeId, amountFils) {
      const row = one(await sb.rpc('create_redemption', { p_store: storeId, p_amount: amountFils }).single());
      emit();
      return toRedemption(row);
    },
    async getRedemption(id) {
      const row = check(await sb.from('redemptions').select('*').eq('id', id).maybeSingle());
      if (!row) throw new ApiError('not_found');
      return toRedemption(row);
    },

    async listNotifications() {
      await uid();
      const rows = list(await sb.from('notifications').select('*').order('created_at', { ascending: false }).limit(100));
      const titles = await productTitles([...new Set(rows.map((r) => r.payload?.product_id).filter(Boolean))] as string[]);
      return rows.map((r) => toNotification(r, titles));
    },
    async markNotificationsRead() {
      await uid();
      check(await sb.from('notifications').update({ read_at: new Date().toISOString() }).is('read_at', null));
      emit();
    },
    async registerPushToken(token, platform) {
      const me = await uid();
      check(await sb.from('push_tokens').upsert({ token, user_id: me, platform }, { onConflict: 'token' }));
    },
    async deleteAccount() {
      const { data, error } = await sb.functions.invoke('delete-account', { method: 'POST' });
      if (error || !data?.ok) {
        // the function answers 409 with { error: code }; supabase-js puts the body on error.context
        let code = data?.error;
        try { code ??= (await (error as { context?: Response })?.context?.json())?.error; } catch { /* ignore */ }
        throw new ApiError(errorCode(code ?? error?.message), code);
      }
      await sb.auth.signOut();
    },

    // ---------------------------------------------------------------- the owner's store
    async listStoreProducts() { return list(await sb.from('store_products').select('*').eq('active', true).order('created_at', { ascending: false })).map(toStoreProduct); },
    async getStoreProduct(id) { return toStoreProduct(one(await sb.from('store_products').select('*').eq('id', id).maybeSingle())); },
    async checkoutStore(items, addressId, key) {
      const row = one(await sb.rpc('checkout_store', { p_items: items.map((i) => ({ product_id: i.productId, qty: i.qty })), p_address: addressId, p_key: key }).single());
      emit();
      return toStoreOrder({ ...row, store_order_items: items.map((i) => ({ product_id: i.productId, title_ar: '', qty: i.qty, unit_price_fils: 0 })) });
    },
    async listMyStoreOrders() {
      const me = await uid();
      return list(await sb.from('store_orders').select('*, store_order_items(*)').eq('user_id', me).order('created_at', { ascending: false })).map(toStoreOrder);
    },

    // ---------------------------------------------------------------- admin (the database refuses non-admins)
    async adminListProducts() { await uid(); return list(await sb.from('products').select('*').order('created_at', { ascending: false })).map(toProduct); },
    async adminSaveProduct(p) { const r = one(await sb.rpc('admin_save_product', { p: fromDrawProduct(p) }).single()); emit(); return toProduct(r); },
    async adminOpenProduct(id) { const r = one(await sb.rpc('admin_open_product', { p_product: id }).single()); emit(); return toProduct(r); },
    async adminCancelProduct(id) { check(await sb.rpc('admin_cancel_product', { p_product: id })); emit(); },
    async adminListStoreProducts() { await uid(); return list(await sb.from('store_products').select('*').order('created_at', { ascending: false })).map(toStoreProduct); },
    async adminSaveStoreProduct(p) { const r = one(await sb.rpc('admin_save_store_product', { p: fromStoreProduct(p) }).single()); emit(); return toStoreProduct(r); },
    async adminListOrders() {
      await uid();
      const [draw, store] = await Promise.all([
        sb.from('orders').select('id, status, created_at, addresses(governorate, city, street), profiles(phone), assignments(products(title_ar))').order('created_at', { ascending: false }).limit(200),
        sb.from('store_orders').select('id, status, created_at, total_fils, store_order_items(title_ar, qty), addresses(governorate, city, street), profiles(phone)').order('created_at', { ascending: false }).limit(200),
      ]);
      const d = list(draw).map((r) => ({ kind: 'draw' as const, id: r.id, status: r.status, createdAt: r.created_at,
        title: r.assignments?.products?.title_ar ?? '', customer: { phone: r.profiles?.phone ?? '', address: addressLine(r.addresses) } }));
      const s = list(store).map((r) => ({ kind: 'store' as const, id: r.id, status: r.status, createdAt: r.created_at, totalFils: Number(r.total_fils),
        title: (r.store_order_items ?? []).map((i: { title_ar: string; qty: number }) => `${i.title_ar} × ${i.qty}`).join('، '),
        customer: { phone: r.profiles?.phone ?? '', address: addressLine(r.addresses) } }));
      return [...d, ...s].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    },
    async adminSetOrderStatus(kind, id, status) {
      check(await sb.rpc(kind === 'draw' ? 'admin_set_order_status' : 'admin_set_store_order_status', { p_order: id, p_status: status }));
      emit();
    },
    // Photos go to the public "product-images" bucket; only admins may upload (storage policy).
    async uploadProductImage(localUri) {
      const body = await (await fetch(localUri)).arrayBuffer();
      const path = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}.jpg`;
      const { error } = await sb.storage.from('product-images').upload(path, body, { contentType: 'image/jpeg', upsert: false });
      if (error) throw new ApiError(errorCode(error.message), error.message);
      return sb.storage.from('product-images').getPublicUrl(path).data.publicUrl;
    },

    subscribe(l) { listeners.add(l); return () => listeners.delete(l); },
  };
  return api;
}
