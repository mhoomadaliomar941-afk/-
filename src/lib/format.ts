import type { OrderStatus, Product } from './types';
import { productArt } from './art';

export const money = (v: number, currency: string) =>
  `${v.toLocaleString('ar-EG-u-nu-latn', { maximumFractionDigits: 2 })} ${currency}`;

export const imageOf = (p: Product, i = 0) => p.images[i] || productArt(p.icon, p.hue);

export const discountPct = (p: Product) =>
  p.oldPrice && p.oldPrice > p.price ? Math.round((1 - p.price / p.oldPrice) * 100) : 0;

export const STATUS: Record<OrderStatus, { label: string; tone: string }> = {
  pending: { label: 'قيد المراجعة', tone: 'amber' },
  processing: { label: 'قيد التجهيز', tone: 'blue' },
  shipped: { label: 'تم الشحن', tone: 'violet' },
  delivered: { label: 'تم التوصيل', tone: 'green' },
  cancelled: { label: 'ملغي', tone: 'red' }
};

export const date = (t: number) =>
  new Date(t).toLocaleDateString('ar-EG-u-nu-latn', { year: 'numeric', month: 'short', day: 'numeric' });
