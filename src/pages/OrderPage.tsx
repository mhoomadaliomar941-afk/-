import { Link, useParams } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { STATUS, date, money } from '../lib/format';
import type { OrderStatus } from '../lib/types';
import Icon from '../components/Icon';

const FLOW: OrderStatus[] = ['pending', 'processing', 'shipped', 'delivered'];

export default function OrderPage() {
  const { id } = useParams();
  const { orders, settings } = useStore();
  const o = orders.find(x => x.id === id);
  if (!o) return <div className="container page empty"><h3>الطلب غير موجود</h3><Link to="/" className="btn primary">الرئيسية</Link></div>;
  const idx = FLOW.indexOf(o.status);

  return (
    <div className="container page narrow">
      <div className="success">
        <span className="success-icon"><Icon name="check" size={36} /></span>
        <h1>شكراً لك، {o.customer.name.split(' ')[0]}!</h1>
        <p className="muted">تم استلام طلبك رقم <b dir="ltr">#{o.id}</b> بتاريخ {date(o.createdAt)}</p>
      </div>

      {o.status === 'cancelled' ? <div className="alert">تم إلغاء هذا الطلب</div> : (
        <div className="tracker">
          {FLOW.map((s, i) => <div key={s} className={i <= idx ? 'done' : ''}><i>{i < idx ? <Icon name="check" size={14} /> : i + 1}</i><span>{STATUS[s].label}</span></div>)}
        </div>
      )}

      <section className="panel">
        <h3>تفاصيل الطلب</h3>
        {o.items.map((i, k) => (
          <div key={k} className="row"><span>{i.name} <small className="muted">{[i.color, i.size].filter(Boolean).join(' · ')} × {i.qty}</small></span><span>{money(i.price * i.qty, settings.currency)}</span></div>
        ))}
        <hr />
        <div className="row"><span>المجموع الفرعي</span><span>{money(o.subtotal, settings.currency)}</span></div>
        {o.discount > 0 && <div className="row green"><span>الخصم</span><span>-{money(o.discount, settings.currency)}</span></div>}
        <div className="row"><span>الشحن</span><span>{o.shipping ? money(o.shipping, settings.currency) : 'مجاني'}</span></div>
        <div className="row total"><span>الإجمالي</span><span>{money(o.total, settings.currency)}</span></div>
      </section>
      <section className="panel">
        <h3>التوصيل إلى</h3>
        <p>{o.customer.name} · <span dir="ltr">{o.customer.phone}</span><br />{o.customer.city} — {o.customer.address}</p>
        <p className="muted">طريقة الدفع: {o.payment === 'cod' ? 'الدفع عند الاستلام' : 'بطاقة ائتمان'}</p>
      </section>
      <div className="hero-cta center"><Link to="/shop" className="btn primary">متابعة التسوّق</Link><Link to="/account" className="btn outline">طلباتي</Link></div>
    </div>
  );
}
