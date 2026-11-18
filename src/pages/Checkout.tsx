import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { imageOf, money } from '../lib/format';
import type { Coupon, Customer } from '../lib/types';
import Icon from '../components/Icon';

export default function Checkout() {
  const { cart, getProduct, cartSubtotal, settings, totals, findCoupon, placeOrder, user, toast } = useStore();
  const nav = useNavigate();
  const [c, setC] = useState<Customer>({ name: user?.name ?? '', phone: user?.phone ?? '', city: '', address: '', notes: '' });
  const [payment, setPayment] = useState<'cod' | 'card'>('cod');
  const [code, setCode] = useState('');
  const [coupon, setCoupon] = useState<Coupon>();
  const [err, setErr] = useState('');
  const [card, setCard] = useState({ number: '', exp: '', cvc: '' });
  const t = totals(cartSubtotal, coupon);

  if (!cart.length) return <div className="container page empty"><h3>سلتك فارغة</h3><Link to="/shop" className="btn primary">تسوّق الآن</Link></div>;

  const apply = () => {
    const found = findCoupon(code);
    if (!found) { setCoupon(undefined); setErr('كود الخصم غير صالح'); return; }
    setErr(''); setCoupon(found); toast('تم تطبيق كود الخصم');
  };

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!/^[+\d\s-]{8,}$/.test(c.phone)) return setErr('يرجى إدخال رقم جوال صحيح');
    if (payment === 'card' && (card.number.replace(/\s/g, '').length < 12 || !card.exp || card.cvc.length < 3))
      return setErr('يرجى إدخال بيانات البطاقة كاملة');
    const order = placeOrder(c, payment, coupon);
    nav(`/order/${order.id}`, { replace: true });
  };

  const f = (k: keyof Customer) => ({ value: c[k] ?? '', onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setC({ ...c, [k]: e.target.value }) });

  return (
    <div className="container page">
      <div className="page-head"><h1>إتمام الطلب</h1></div>
      <form className="two-col" onSubmit={submit}>
        <div className="stack">
          <section className="panel">
            <h3><span className="step">1</span> بيانات التوصيل</h3>
            <div className="form-grid">
              <label>الاسم الكامل<input required {...f('name')} /></label>
              <label>رقم الجوال<input required type="tel" inputMode="tel" placeholder="05xxxxxxxx" {...f('phone')} /></label>
              <label>المدينة<input required {...f('city')} /></label>
              <label className="full">العنوان التفصيلي<input required placeholder="الحي، الشارع، رقم المبنى" {...f('address')} /></label>
              <label className="full">ملاحظات (اختياري)<textarea rows={2} {...f('notes')} /></label>
            </div>
          </section>
          <section className="panel">
            <h3><span className="step">2</span> طريقة الدفع</h3>
            <div className="pay">
              <label className={payment === 'cod' ? 'on' : ''}><input type="radio" checked={payment === 'cod'} onChange={() => setPayment('cod')} /> <Icon name="truck" /> الدفع عند الاستلام</label>
              <label className={payment === 'card' ? 'on' : ''}><input type="radio" checked={payment === 'card'} onChange={() => setPayment('card')} /> <Icon name="shield" /> بطاقة ائتمان / مدى</label>
            </div>
            {payment === 'card' && (
              <div className="form-grid">
                <label className="full">رقم البطاقة<input inputMode="numeric" dir="ltr" placeholder="4242 4242 4242 4242" value={card.number}
                  onChange={e => setCard({ ...card, number: e.target.value.replace(/[^\d]/g, '').slice(0, 16).replace(/(.{4})/g, '$1 ').trim() })} /></label>
                <label>تاريخ الانتهاء<input dir="ltr" placeholder="MM/YY" value={card.exp} onChange={e => setCard({ ...card, exp: e.target.value.slice(0, 5) })} /></label>
                <label>CVC<input dir="ltr" inputMode="numeric" placeholder="123" value={card.cvc} onChange={e => setCard({ ...card, cvc: e.target.value.replace(/\D/g, '').slice(0, 4) })} /></label>
                <p className="muted full small">🔒 وضع تجريبي: لا يتم خصم أي مبلغ. اربط بوابة دفع (Stripe / Moyasar / Tap) قبل الإطلاق.</p>
              </div>
            )}
          </section>
        </div>
        <aside className="summary">
          <h3>طلبك</h3>
          <div className="mini-lines">
            {cart.map(i => { const p = getProduct(i.productId)!; return (
              <div key={i.key} className="mini"><img src={imageOf(p)} alt="" /><div><span>{p.name}</span><small className="muted">{i.qty} × {money(p.price, settings.currency)}</small></div></div>
            ); })}
          </div>
          <div className="coupon">
            <input value={code} onChange={e => setCode(e.target.value)} placeholder="كود الخصم" />
            <button type="button" className="btn outline" onClick={apply}>تطبيق</button>
          </div>
          <div className="row"><span>المجموع الفرعي</span><span>{money(cartSubtotal, settings.currency)}</span></div>
          {t.discount > 0 && <div className="row green"><span>الخصم ({coupon?.code})</span><span>-{money(t.discount, settings.currency)}</span></div>}
          <div className="row"><span>الشحن</span><span>{t.shipping ? money(t.shipping, settings.currency) : 'مجاني'}</span></div>
          <div className="row total"><span>الإجمالي</span><span>{money(t.total, settings.currency)}</span></div>
          {err && <div className="alert">{err}</div>}
          <button className="btn primary block" type="submit">تأكيد الطلب · {money(t.total, settings.currency)}</button>
        </aside>
      </form>
    </div>
  );
}
