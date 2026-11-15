// Mirrors replica/schema.sql. Money is integer fils.
export type ProductStatus = 'draft' | 'open' | 'full' | 'assigned' | 'delivered' | 'cancelled';
export type Category = 'phone' | 'console' | 'watch' | 'audio' | 'laptop' | 'tablet';

export interface Product {
  id: string; titleAr: string; brand: string | null; category: Category; descriptionAr: string;
  specs: Record<string, string>; retailPriceFils: number; spotPriceFils: number;
  totalSpots: number; filledSpots: number; maxSpotsPerUser: number | null;
  status: ProductStatus; closesAt: string | null; seedHash: string;
}
export interface Participation {
  id: string; userId: string; productId: string; spots: number; amountFils: number;
  status: 'active' | 'owner' | 'credited' | 'refunded'; createdAt: string; spotNumbers: number[];
}
export interface Assignment {
  id: string; productId: string; winningSpot: number; ownerId: string; ownerName: string;
  serverSeed: string; beaconRound: number; beaconValue: string; assignedAt: string;
}
export interface Wallet { cashFils: number; shoppingFils: number }
export type TxnType = 'topup' | 'spend' | 'credit' | 'redeem' | 'refund' | 'adjust';
export interface WalletTxn {
  id: string; bucket: 'cash' | 'shopping'; type: TxnType; amountFils: number;
  status: 'pending' | 'done' | 'failed'; label?: string; createdAt: string;
}
export interface Address { id: string; governorate: string; city: string; area: string; street: string; phone: string }
export type OrderStatus = 'awaiting_address' | 'pending' | 'shipped' | 'delivered' | 'failed';
export interface Order { id: string; assignmentId: string; productId: string; addressId: string | null; status: OrderStatus }
export interface Store { id: string; nameAr: string; governorate: string; city: string; category: string }
export interface Redemption { id: string; storeId: string; amountFils: number; code: string; status: 'pending' | 'confirmed' | 'expired' | 'cancelled'; expiresAt: string }
export interface Notification { id: string; type: 'owner' | 'credited' | 'product_open' | 'order_shipped'; title: string; body: string; productId?: string; readAt: string | null; createdAt: string }
export interface ShowcaseItem { id: string; displayName: string; productTitle: string; category: Category; date: string }
export interface Profile { id: string; phone: string; fullName: string | null }
export type PaymentMethod = 'card' | 'apple_pay';

export class ApiError extends Error {
  constructor(public code: 'not_signed_in' | 'not_open' | 'sold_out' | 'limit' | 'insufficient' | 'not_found' | 'invalid' | 'wrong_otp', message?: string) {
    super(message ?? code);
  }
}

/** The contract both the fake and the Supabase implementation follow. Screens only use this. */
export interface Api {
  sendOtp(phone: string): Promise<void>;
  verifyOtp(phone: string, code: string): Promise<Profile>;
  me(): Promise<Profile | null>;
  signOut(): Promise<void>;
  listProducts(): Promise<Product[]>;
  getProduct(id: string): Promise<Product>;
  mySpotsIn(productId: string): Promise<number>;
  buySpots(productId: string, spots: number, idempotencyKey: string): Promise<Participation>;
  startTopup(amountFils: number, method: PaymentMethod): Promise<{ paymentId: string }>;
  getWallet(): Promise<Wallet>;
  listTxns(): Promise<WalletTxn[]>;
  listParticipations(): Promise<(Participation & { product: Product })[]>;
  getAssignment(productId: string): Promise<Assignment | null>;
  listAddresses(): Promise<Address[]>;
  addAddress(a: Omit<Address, 'id'>): Promise<Address>;
  getOrderForProduct(productId: string): Promise<Order | null>;
  setOrderAddress(orderId: string, addressId: string): Promise<Order>;
  listShowcase(): Promise<ShowcaseItem[]>;
  listStores(): Promise<Store[]>;
  getStore(id: string): Promise<Store>;
  createRedemption(storeId: string, amountFils: number): Promise<Redemption>;
  getRedemption(id: string): Promise<Redemption>;
  listNotifications(): Promise<Notification[]>;
  markNotificationsRead(): Promise<void>;
  subscribe(listener: () => void): () => void;
}
