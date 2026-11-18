import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { discountPct, imageOf, money } from '../lib/format';
import ProductCard from '../components/ProductCard';
import Icon from '../components/Icon';

export default function ProductPage() {
  const { id } = useParams();
  const { getProduct, products, categories, settings, addToCart, toggleWish, wishlist, toast } = useStore();
  const p = getProduct(id ?? '');
  const nav = useNavigate();
  const [img, setImg] = useState(0);
  const [size, setSize] = useState<string>();
  const [color, setColor] = useState<string>();
  const [qty, setQty] = useState(1);
  const [err, setErr] = useState('');

  if (!p) return <div className="container page empty"><h3>المنتج غير موجود</h3><Link to="/shop" className="btn primary">العودة للمتجر</Link></div>;

  const cat = categories.find(c => c.id === p.category);
  const off = discountPct(p);
  const related = products.filter(x => x.category === p.category && x.id !== p.id).slice(0, 4);
  const imgs = p.images.length ? p.images : [imageOf(p)];

  const add = (buy = false) => {
    if (p.sizes?.length && !size) return setErr('يرجى اختيار المقاس');
    if (p.colors?.length && !color) return setErr('يرجى اختيار اللون');
    setErr('');
    addToCart(p.id, qty, size, color);
    if (buy) nav('/checkout');
  };

  const share = async () => {
    const url = window.location.href;
    try {
      if (navigator.share) await navigator.share({ title: p.name, url });
      else { await navigator.clipboard.writeText(url); toast('تم نسخ رابط المنتج'); }
    } catch { /* cancelled */ }
  };

  return (
    <div className="container page">
      <nav className="crumbs"><Link to="/">الرئيسية</Link> / <Link to={`/shop?cat=${p.category}`}>{cat?.name}</Link> / <span>{p.name}</span></nav>
      <div className="pdp">
        <div className="gallery">
          <div className="gallery-main">
            <img src={imgs[img]} alt={p.name} />
            {off > 0 && <span className="badge sale">-{off}%</span>}
          </div>
          {imgs.length > 1 && (
            <div className="thumbs">{imgs.map((s, i) => <button key={i} className={i === img ? 'on' : ''} onClick={() => setImg(i)}><img src={s} alt="" /></button>)}</div>
          )}
        </div>
        <div className="pdp-info">
          <span className="eyebrow">{cat?.name}</span>
          <h1>{p.name}</h1>
          <div className="rating big"><Icon name="star" size={16} fill /> {p.rating} <span>· {p.reviews} تقييم</span></div>
          <div className="price big">
            <strong>{money(p.price, settings.currency)}</strong>
            {p.oldPrice && <del>{money(p.oldPrice, settings.currency)}</del>}
            {off > 0 && <span className="save">وفّر {money(p.oldPrice! - p.price, settings.currency)}</span>}
          </div>
          <p className="desc">{p.description}</p>

          {p.colors?.length ? (
            <div className="opt"><label>اللون: <b>{color ?? ''}</b></label>
              <div className="opts">{p.colors.map(c => <button key={c} className={c === color ? 'on' : ''} onClick={() => setColor(c)}>{c}</button>)}</div>
            </div>
          ) : null}
          {p.sizes?.length ? (
            <div className="opt"><label>المقاس: <b>{size ?? ''}</b></label>
              <div className="opts">{p.sizes.map(s => <button key={s} className={s === size ? 'on' : ''} onClick={() => setSize(s)}>{s}</button>)}</div>
            </div>
          ) : null}

          <div className="stock">{p.stock === 0 ? <span className="red">نفدت الكمية</span> : p.stock < 10 ? <span className="amber">متبقٍ {p.stock} قطع فقط!</span> : <span className="green">متوفر في المخزون</span>}</div>
          {err && <div className="alert">{err}</div>}

          <div className="buy-row">
            <div className="qty">
              <button onClick={() => setQty(q => q + 1)} disabled={qty >= p.stock}><Icon name="plus" size={16} /></button>
              <span>{qty}</span>
              <button onClick={() => setQty(q => Math.max(1, q - 1))}><Icon name="minus" size={16} /></button>
            </div>
            <button className="btn primary grow" disabled={p.stock === 0} onClick={() => add()}>أضف إلى السلة</button>
            <button className={`icon-btn lg ${wishlist.includes(p.id) ? 'on' : ''}`} onClick={() => toggleWish(p.id)} aria-label="المفضلة"><Icon name="heart" fill={wishlist.includes(p.id)} /></button>
            <button className="icon-btn lg" onClick={share} aria-label="مشاركة"><Icon name="share" /></button>
          </div>
          <button className="btn dark block" disabled={p.stock === 0} onClick={() => add(true)}>اشترِ الآن</button>

          <ul className="perks">
            <li><Icon name="truck" size={18} /> شحن مجاني للطلبات فوق {settings.freeShippingOver} {settings.currency}</li>
            <li><Icon name="refresh" size={18} /> استبدال واسترجاع خلال 14 يوماً</li>
            <li><Icon name="shield" size={18} /> منتج أصلي 100% مع ضمان</li>
          </ul>
        </div>
      </div>

      {related.length > 0 && (
        <section className="section">
          <div className="section-head"><h2>قد يعجبك أيضاً</h2></div>
          <div className="grid">{related.map(x => <ProductCard key={x.id} p={x} />)}</div>
        </section>
      )}
    </div>
  );
}
