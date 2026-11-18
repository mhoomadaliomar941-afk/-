import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import ProductCard from '../components/ProductCard';
import Icon from '../components/Icon';

const SORTS: Record<string, string> = { popular: 'الأكثر شيوعاً', new: 'الأحدث', low: 'السعر: من الأقل', high: 'السعر: من الأعلى', rating: 'الأعلى تقييماً' };

export default function Shop() {
  const { products, categories } = useStore();
  const [sp, setSp] = useSearchParams();
  const cat = sp.get('cat') ?? '';
  const q = sp.get('q') ?? '';
  const sort = sp.get('sort') ?? 'popular';
  const sale = sp.get('sale') === '1';
  const max = Number(sp.get('max') ?? 0);

  const set = (k: string, v: string) => {
    const n = new URLSearchParams(sp);
    if (v) n.set(k, v); else n.delete(k);
    setSp(n, { replace: true });
  };

  const list = useMemo(() => {
    const term = q.trim().toLowerCase();
    let r = products.filter(p =>
      (!cat || p.category === cat) &&
      (!sale || (p.oldPrice && p.oldPrice > p.price)) &&
      (!max || p.price <= max) &&
      (!term || p.name.toLowerCase().includes(term) || p.description.toLowerCase().includes(term))
    );
    const by: Record<string, (a: typeof r[0], b: typeof r[0]) => number> = {
      popular: (a, b) => b.reviews - a.reviews,
      new: (a, b) => b.createdAt - a.createdAt,
      low: (a, b) => a.price - b.price,
      high: (a, b) => b.price - a.price,
      rating: (a, b) => b.rating - a.rating
    };
    r = [...r].sort(by[sort] ?? by.popular);
    return r;
  }, [products, cat, q, sort, sale, max]);

  const title = categories.find(c => c.id === cat)?.name ?? (sale ? 'العروض والخصومات' : q ? `نتائج البحث عن "${q}"` : 'جميع المنتجات');

  return (
    <div className="container page">
      <div className="page-head">
        <h1>{title}</h1>
        <span className="muted">{list.length} منتج</span>
      </div>

      <div className="shop-search">
        <Icon name="search" size={18} />
        <input value={q} onChange={e => set('q', e.target.value)} placeholder="ابحث باسم المنتج أو الوصف..." />
        {q && <button onClick={() => set('q', '')} aria-label="مسح"><Icon name="close" size={16} /></button>}
      </div>

      <div className="chips">
        <button className={!cat ? 'chip on' : 'chip'} onClick={() => set('cat', '')}>الكل</button>
        {categories.map(c => (
          <button key={c.id} className={cat === c.id ? 'chip on' : 'chip'} onClick={() => set('cat', c.id)}>{c.name}</button>
        ))}
      </div>

      <div className="toolbar">
        <label className="select"><Icon name="filter" size={16} />
          <select value={sort} onChange={e => set('sort', e.target.value)}>
            {Object.entries(SORTS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </label>
        <label className="select">
          <select value={max || ''} onChange={e => set('max', e.target.value)}>
            <option value="">كل الأسعار</option>
            <option value="100">حتى 100</option>
            <option value="250">حتى 250</option>
            <option value="500">حتى 500</option>
            <option value="1000">حتى 1000</option>
          </select>
        </label>
        <label className="toggle"><input type="checkbox" checked={sale} onChange={e => set('sale', e.target.checked ? '1' : '')} /> عروض فقط</label>
      </div>

      {list.length ? (
        <div className="grid">{list.map(p => <ProductCard key={p.id} p={p} />)}</div>
      ) : (
        <div className="empty"><Icon name="search" size={40} /><h3>لا توجد نتائج</h3><p>جرّب كلمات بحث أخرى أو أزل بعض الفلاتر.</p></div>
      )}
    </div>
  );
}
