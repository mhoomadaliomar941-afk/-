import { Link } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { imageOf, money } from '../lib/format';
import Icon from '../components/Icon';

export default function Cart() {
  const { cart, getProduct, setQty, removeFromCart, cartSubtotal, settings, totals } = useStore();
  const t = totals(cartSubtotal);
  const remaining = settings.freeShippingOver - cartSubtotal;

  if (!cart.length) return (
    <div className="container page empty">
      <Icon name="cart" size={48} /><h3>سلتك فارغة</h3><p>لم تُضف أي منتجات بعد، اكتشف تشكيلتنا المميزة.</p>
      <Link to="/shop" className="btn primary">ابدأ التسوّق</Link>
    </div>
  );

  return (
    <div className="container page">
      <div className="page-head"><h1>سلة التسوّق</h1><span className="muted">{cart.length} منتج</span></div>
      <div className="two-col">
        <div className="lines">
          {remaining > 0 ? (
            <div className="ship-bar"><span>أضف {money(remaining, settings.currency)} لتحصل على شحن مجاني</span>
              <div><i style={{ width: `${Math.min(100, (cartSubtotal / settings.freeShippingOver) * 100)}%` }} /></div></div>
          ) : <div className="ship-bar ok"><Icon name="check" size={18} /> مبروك! طلبك مؤهل للشحن المجاني</div>}
          {cart.map(i => {
            const p = getProduct(i.productId);
            if (!p) return null;
            return (
              <div key={i.key} className="line">
                <Link to={`/product/${p.id}`}><img src={imageOf(p)} alt={p.name} /></Link>
                <div className="line-info">
                  <Link to={`/product/${p.id}`} className="line-title">{p.name}</Link>
                  <small className="muted">{[i.color, i.size].filter(Boolean).join(' · ')}</small>
                  <strong>{money(p.price, settings.currency)}</strong>
                </div>
                <div className="line-actions">
                  <div className="qty sm">
                    <button onClick={() => setQty(i.key, i.qty + 1)} disabled={i.qty >= p.stock}><Icon name="plus" size={14} /></button>
                    <span>{i.qty}</span>
                    <button onClick={() => setQty(i.key, i.qty - 1)}><Icon name="minus" size={14} /></button>
                  </div>
                  <button className="link-btn" onClick={() => removeFromCart(i.key)}><Icon name="trash" size={16} /> حذف</button>
                </div>
              </div>
            );
          })}
        </div>
        <aside className="summary">
          <h3>ملخص الطلب</h3>
          <div className="row"><span>المجموع الفرعي</span><span>{money(cartSubtotal, settings.currency)}</span></div>
          <div className="row"><span>الشحن</span><span>{t.shipping ? money(t.shipping, settings.currency) : 'مجاني'}</span></div>
          <div className="row total"><span>الإجمالي</span><span>{money(t.total, settings.currency)}</span></div>
          <Link to="/checkout" className="btn primary block">إتمام الطلب</Link>
          <Link to="/shop" className="btn outline block">متابعة التسوّق</Link>
        </aside>
      </div>
    </div>
  );
}
