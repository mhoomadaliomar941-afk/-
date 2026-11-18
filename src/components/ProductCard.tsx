import { Link } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { discountPct, imageOf, money } from '../lib/format';
import type { Product } from '../lib/types';
import Icon from './Icon';

export default function ProductCard({ p }: { p: Product }) {
  const { settings, wishlist, toggleWish, addToCart } = useStore();
  const off = discountPct(p);
  const wished = wishlist.includes(p.id);
  const needsOptions = (p.sizes?.length ?? 0) > 0 || (p.colors?.length ?? 0) > 0;
  return (
    <article className="card">
      <Link to={`/product/${p.id}`} className="card-img">
        <img src={imageOf(p)} alt={p.name} loading="lazy" />
        <div className="badges">
          {off > 0 && <span className="badge sale">-{off}%</span>}
          {p.isNew && <span className="badge new">جديد</span>}
          {p.stock === 0 && <span className="badge out">نفد</span>}
        </div>
      </Link>
      <button className={`wish ${wished ? 'on' : ''}`} onClick={() => toggleWish(p.id)} aria-label="المفضلة">
        <Icon name="heart" size={18} fill={wished} />
      </button>
      <div className="card-body">
        <Link to={`/product/${p.id}`} className="card-title">{p.name}</Link>
        <div className="rating"><Icon name="star" size={14} fill /> {p.rating} <span>({p.reviews})</span></div>
        <div className="card-foot">
          <div className="price">
            <strong>{money(p.price, settings.currency)}</strong>
            {p.oldPrice && <del>{money(p.oldPrice, settings.currency)}</del>}
          </div>
          {needsOptions ? (
            <Link to={`/product/${p.id}`} className="icon-btn" aria-label="اختر الخيارات"><Icon name="cart" size={18} /></Link>
          ) : (
            <button className="icon-btn" disabled={p.stock === 0} onClick={() => addToCart(p.id)} aria-label="أضف للسلة">
              <Icon name="cart" size={18} />
            </button>
          )}
        </div>
      </div>
    </article>
  );
}
