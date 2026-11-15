// Expo push service (official): https://exp.host/--/api/v2/push/send, up to 100 messages per request.
export interface PushMessage { to: string; title: string; body: string; data?: Record<string, unknown> }

export const pushText: Record<string, (p: Record<string, unknown>) => { title: string; body: string }> = {
  owner: () => ({ title: 'مبروك! صار المنتج إلك', body: 'افتح التطبيق وأدخل عنوان التوصيل.' }),
  credited: () => ({ title: 'رجعلك رصيد تسوق', body: 'استخدمه بأي متجر شريك.' }),
  product_open: () => ({ title: 'منتج جديد', body: 'نزل منتج جديد، الأماكن محدودة.' }),
  order_shipped: () => ({ title: 'طلبك بالطريق', body: 'المنتج انشحن لعنوانك.' }),
};

export function chunk<T>(xs: T[], n = 100): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < xs.length; i += n) out.push(xs.slice(i, i + n));
  return out;
}

export async function sendPush(messages: PushMessage[], f: typeof fetch = fetch) {
  const results: unknown[] = [];
  for (const batch of chunk(messages)) {
    const res = await f('https://exp.host/--/api/v2/push/send', {
      method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(batch.map((m) => ({ ...m, sound: 'default' }))),
    });
    if (!res.ok) throw new Error(`expo push ${res.status}`);
    results.push(await res.json());
  }
  return results;
}
