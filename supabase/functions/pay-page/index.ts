// GET ?id=<checkoutId>&m=card|apple_pay -> HTML page hosting HyperPay's own card form.
// The card number is typed into HyperPay's widget, never into our app or servers.
import { env } from '../_shared/env.ts';

Deno.serve((req) => {
  const url = new URL(req.url);
  const id = url.searchParams.get('id') ?? '';
  const brands = url.searchParams.get('m') === 'apple_pay' ? 'APPLEPAY' : 'VISA MASTER';
  if (!/^[A-Za-z0-9.\-]{8,64}$/.test(id)) return new Response('bad request', { status: 400 });
  const base = env('HYPERPAY_BASE_URL');
  const back = `${env('SUPABASE_URL')}/functions/v1/topup-return`;
  const html = `<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>الدفع</title>
<style>body{font-family:system-ui,sans-serif;margin:0;padding:16px;background:#fff;color:#111418}
h1{font-size:20px}p{color:#58606d}.wpwl-form{max-width:420px;margin:0 auto}</style>
<script>var wpwlOptions={locale:"ar",style:"plain"};</script>
<script src="${base}/v1/paymentWidgets.js?checkoutId=${id}"></script></head>
<body><h1>شحن المحفظة</h1><p>بيانات البطاقة بتنكتب عند بوابة الدفع مباشرة.</p>
<form action="${back}" class="paymentWidgets" data-brands="${brands}"></form></body></html>`;
  return new Response(html, { headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } });
});
