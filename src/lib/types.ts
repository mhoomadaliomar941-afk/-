export type IconKey = 'shirt' | 'dress' | 'headphones' | 'watch' | 'perfume' | 'lamp' | 'sneaker' | 'bag' | 'phone' | 'glasses';

export interface Category {
  id: string;
  name: string;
  icon: IconKey;
  hue: number;
}

export interface Product {
  id: string;
  name: string;
  category: string;
  price: number;
  oldPrice?: number;
  description: string;
  images: string[];
  icon: IconKey;
  hue: number;
  rating: number;
  reviews: number;
  stock: number;
  sizes?: string[];
  colors?: string[];
  featured?: boolean;
  isNew?: boolean;
  createdAt: number;
}

export interface CartItem {
  key: string;
  productId: string;
  qty: number;
  size?: string;
  color?: string;
}

export type OrderStatus = 'pending' | 'processing' | 'shipped' | 'delivered' | 'cancelled';

export interface OrderItem {
  productId: string;
  name: string;
  price: number;
  qty: number;
  size?: string;
  color?: string;
}

export interface Customer {
  name: string;
  phone: string;
  city: string;
  address: string;
  notes?: string;
}

export interface Order {
  id: string;
  items: OrderItem[];
  subtotal: number;
  shipping: number;
  discount: number;
  total: number;
  coupon?: string;
  customer: Customer;
  payment: 'cod' | 'card';
  status: OrderStatus;
  createdAt: number;
  userEmail?: string;
}

export interface User {
  name: string;
  email: string;
  phone?: string;
  password: string;
  isAdmin?: boolean;
}

export interface Coupon {
  code: string;
  type: 'percent' | 'fixed';
  value: number;
  active: boolean;
}

export interface Settings {
  storeName: string;
  currency: string;
  shippingFee: number;
  freeShippingOver: number;
  whatsapp: string;
  email: string;
  announcement: string;
}
