import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ADMIN, CATEGORIES, COUPONS, PRODUCTS, SETTINGS } from '../data/seed';
import type { CartItem, Category, Coupon, Customer, Order, OrderStatus, Product, Settings, User } from '../lib/types';

/*
 * Single source of truth for the store. Everything is persisted locally so the
 * app works fully offline (web, Android, iOS). To go multi-user, replace the
 * load/save functions with calls to your backend (Firebase, Supabase, ...).
 */

interface State {
  products: Product[];
  categories: Category[];
  coupons: Coupon[];
  settings: Settings;
  users: User[];
  orders: Order[];
  cart: CartItem[];
  wishlist: string[];
  session: string | null; // logged-in email
}

const KEY = 'zeno:v1';

function initial(): State {
  return {
    products: PRODUCTS,
    categories: CATEGORIES,
    coupons: COUPONS,
    settings: SETTINGS,
    users: [ADMIN],
    orders: [],
    cart: [],
    wishlist: [],
    session: null
  };
}

function load(): State {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return { ...initial(), ...JSON.parse(raw) };
  } catch { /* ignore */ }
  return initial();
}

export interface Toast { id: number; text: string }

interface Ctx extends State {
  user: User | null;
  toasts: Toast[];
  toast: (text: string) => void;
  getProduct: (id: string) => Product | undefined;
  // cart
  addToCart: (productId: string, qty?: number, size?: string, color?: string) => void;
  setQty: (key: string, qty: number) => void;
  removeFromCart: (key: string) => void;
  clearCart: () => void;
  cartCount: number;
  cartSubtotal: number;
  // wishlist
  toggleWish: (id: string) => void;
  // pricing
  findCoupon: (code: string) => Coupon | undefined;
  totals: (subtotal: number, coupon?: Coupon) => { shipping: number; discount: number; total: number };
  // orders
  placeOrder: (customer: Customer, payment: Order['payment'], coupon?: Coupon) => Order;
  setOrderStatus: (id: string, status: OrderStatus) => void;
  // auth
  login: (email: string, password: string) => string | null;
  register: (u: User) => string | null;
  logout: () => void;
  updateProfile: (u: Partial<User>) => void;
  // admin
  saveProduct: (p: Product) => void;
  deleteProduct: (id: string) => void;
  saveCoupon: (c: Coupon) => void;
  deleteCoupon: (code: string) => void;
  saveSettings: (s: Settings) => void;
  resetStore: () => void;
}

const StoreCtx = createContext<Ctx | null>(null);

export function StoreProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<State>(load);
  const [toasts, setToasts] = useState<Toast[]>([]);

  useEffect(() => {
    try { localStorage.setItem(KEY, JSON.stringify(state)); } catch { /* quota */ }
  }, [state]);

  const patch = useCallback((fn: (s: State) => Partial<State>) => setState(s => ({ ...s, ...fn(s) })), []);

  const toast = useCallback((text: string) => {
    const id = Date.now() + Math.random();
    setToasts(t => [...t, { id, text }]);
    setTimeout(() => setToasts(t => t.filter(x => x.id !== id)), 2600);
  }, []);

  const value = useMemo<Ctx>(() => {
    const getProduct = (id: string) => state.products.find(p => p.id === id);
    const cartLines = state.cart.map(i => ({ i, p: getProduct(i.productId) })).filter(x => x.p);
    const cartSubtotal = cartLines.reduce((s, { i, p }) => s + p!.price * i.qty, 0);
    const cartCount = state.cart.reduce((s, i) => s + i.qty, 0);
    const user = state.users.find(u => u.email === state.session) ?? null;

    const findCoupon = (code: string) =>
      state.coupons.find(c => c.active && c.code.toUpperCase() === code.trim().toUpperCase());

    const totals = (subtotal: number, coupon?: Coupon) => {
      const discount = coupon
        ? Math.min(subtotal, coupon.type === 'percent' ? Math.round(subtotal * coupon.value) / 100 : coupon.value)
        : 0;
      const after = subtotal - discount;
      const shipping = subtotal === 0 || after >= state.settings.freeShippingOver ? 0 : state.settings.shippingFee;
      return { shipping, discount, total: Math.max(0, after + shipping) };
    };

    return {
      ...state,
      user,
      toasts,
      toast,
      getProduct,
      cartCount,
      cartSubtotal,
      addToCart: (productId, qty = 1, size, color) => {
        const key = [productId, size ?? '', color ?? ''].join('|');
        patch(s => {
          const ex = s.cart.find(i => i.key === key);
          return {
            cart: ex
              ? s.cart.map(i => (i.key === key ? { ...i, qty: i.qty + qty } : i))
              : [...s.cart, { key, productId, qty, size, color }]
          };
        });
        toast('تمت الإضافة إلى السلة');
      },
      setQty: (key, qty) => patch(s => ({ cart: s.cart.map(i => (i.key === key ? { ...i, qty: Math.max(1, qty) } : i)) })),
      removeFromCart: key => patch(s => ({ cart: s.cart.filter(i => i.key !== key) })),
      clearCart: () => patch(() => ({ cart: [] })),
      toggleWish: id => {
        const has = state.wishlist.includes(id);
        patch(s => ({ wishlist: has ? s.wishlist.filter(x => x !== id) : [...s.wishlist, id] }));
        toast(has ? 'أُزيل من المفضلة' : 'أُضيف إلى المفضلة');
      },
      findCoupon,
      totals,
      placeOrder: (customer, payment, coupon) => {
        const items = cartLines.map(({ i, p }) => ({
          productId: p!.id, name: p!.name, price: p!.price, qty: i.qty, size: i.size, color: i.color
        }));
        const t = totals(cartSubtotal, coupon);
        const order: Order = {
          id: 'ZN' + Date.now().toString().slice(-8),
          items,
          subtotal: cartSubtotal,
          ...t,
          coupon: coupon?.code,
          customer,
          payment,
          status: 'pending',
          createdAt: Date.now(),
          userEmail: state.session ?? undefined
        };
        patch(s => ({
          orders: [order, ...s.orders],
          cart: [],
          products: s.products.map(p => {
            const sold = items.filter(x => x.productId === p.id).reduce((a, x) => a + x.qty, 0);
            return sold ? { ...p, stock: Math.max(0, p.stock - sold) } : p;
          })
        }));
        return order;
      },
      setOrderStatus: (id, status) => patch(s => ({ orders: s.orders.map(o => (o.id === id ? { ...o, status } : o)) })),
      login: (email, password) => {
        const u = state.users.find(u => u.email.toLowerCase() === email.trim().toLowerCase());
        if (!u || u.password !== password) return 'البريد الإلكتروني أو كلمة المرور غير صحيحة';
        patch(() => ({ session: u.email }));
        toast(`أهلاً ${u.name} 👋`);
        return null;
      },
      register: u => {
        const email = u.email.trim().toLowerCase();
        if (state.users.some(x => x.email.toLowerCase() === email)) return 'هذا البريد مسجّل مسبقاً';
        patch(s => ({ users: [...s.users, { ...u, email, isAdmin: false }], session: email }));
        toast('تم إنشاء حسابك بنجاح');
        return null;
      },
      logout: () => { patch(() => ({ session: null })); toast('تم تسجيل الخروج'); },
      updateProfile: data => patch(s => ({ users: s.users.map(u => (u.email === s.session ? { ...u, ...data, email: u.email } : u)) })),
      saveProduct: prod => patch(s => ({
        products: s.products.some(p => p.id === prod.id)
          ? s.products.map(p => (p.id === prod.id ? prod : p))
          : [prod, ...s.products]
      })),
      deleteProduct: id => patch(s => ({ products: s.products.filter(p => p.id !== id), cart: s.cart.filter(i => i.productId !== id) })),
      saveCoupon: c => patch(s => ({
        coupons: s.coupons.some(x => x.code === c.code) ? s.coupons.map(x => (x.code === c.code ? c : x)) : [...s.coupons, c]
      })),
      deleteCoupon: code => patch(s => ({ coupons: s.coupons.filter(c => c.code !== code) })),
      saveSettings: settings => patch(() => ({ settings })),
      resetStore: () => setState({ ...initial(), session: state.session })
    };
  }, [state, toasts, patch, toast]);

  return <StoreCtx.Provider value={value}>{children}</StoreCtx.Provider>;
}

export function useStore() {
  const c = useContext(StoreCtx);
  if (!c) throw new Error('useStore outside provider');
  return c;
}
