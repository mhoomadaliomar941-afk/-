import { useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { useStore } from '../../store/StoreContext';
import { STATUS, date, imageOf, money } from '../../lib/format';
import type { Coupon, OrderStatus, Product, Settings } from '../../lib/types';
import Icon from '../../components/Icon';

type Tab = 'dash' | 'products' | 'orders' | 'coupons' | 'settings';

export default function Admin() {
  const { user } = useStore();
  const [tab, setTab] = useState<Tab>('dash');
  if (!user?.isAdmin) return <Navigate to="/account" replace />;
  const tabs: [Tab, string, string][] = [
    ['dash', 'chart', 'نظرة عامة'], ['products', 'box', 'المنتجات'], ['orders', 'truck', 'الطلبات'],
    ['coupons', 'tag', 'الكوبونات'], ['settings', 'settings', 'الإعدادات']
  ];
  return (
    <div className="container page admin">
      <div className="page-head"><h1>لوحة التحكم</h1><Link to="/" className="muted">عرض المتجر ←</Link></div>
      <div className="admin-tabs">{tabs.map(([k, i, l]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}><Icon name={i} size={18} />{l}</button>)}</div>
      {tab === 'dash' && <Dashboard go={setTab} />}
      {tab === 'products' && <Products />}
      {tab === 'orders' && <Orders />}
      {tab === 'coupons' && <Coupons />}
      {tab === 'settings' && <SettingsTab />}
    </div>
  );
}

function Dashboard({ go }: { go: (t: Tab) => void }) {
  const { orders, products, users, settings } = useStore();
  const valid = orders.filter(o => o.status !== 'cancelled');
  const revenue = valid.reduce((s, o) => s + o.total, 0);
  const low = products.filter(p => p.stock < 10);
  const sold: Record<string, number> = {};
  valid.forEach(o => o.items.forEach(i => { sold[i.name] = (sold[i.name] ?? 0) + i.qty; }));
  const top = Object.entries(sold).sort((a, b) => b[1] - a[1]).slice(0, 5);
  const days = [...Array(7)].map((_, i) => {
    const d = new Date(); d.setHours(0, 0, 0, 0); d.setDate(d.getDate() - (6 - i));
    const end = d.getTime() + 86400000;
    return { label: d.toLocaleDateString('ar-EG-u-nu-latn', { weekday: 'short' }), v: valid.filter(o => o.createdAt >= d.getTime() && o.createdAt < end).reduce((s, o) => s + o.total, 0) };
  });
  const maxV = Math.max(1, ...days.map(d => d.v));

  return (
    <div className="stack">
      <div className="kpis">
        <div className="kpi"><small>إجمالي المبيعات</small><strong>{money(revenue, settings.currency)}</strong></div>
        <div className="kpi"><small>عدد الطلبات</small><strong>{orders.length}</strong></div>
        <div className="kpi"><small>طلبات قيد المراجعة</small><strong>{orders.filter(o => o.status === 'pending').length}</strong></div>
        <div className="kpi"><small>العملاء المسجلون</small><strong>{users.filter(u => !u.isAdmin).length}</strong></div>
      </div>
      <div className="two-col even">
        <section className="panel">
          <h3>مبيعات آخر 7 أيام</h3>
          <div className="bars">{days.map((d, i) => (
            <div key={i} title={money(d.v, settings.currency)}><i style={{ height: `${(d.v / maxV) * 100}%` }} /><span>{d.label}</span></div>
          ))}</div>
        </section>
        <section className="panel">
          <h3>الأكثر مبيعاً</h3>
          {top.length ? top.map(([n, q]) => <div key={n} className="row"><span>{n}</span><b>{q}</b></div>) : <p className="muted">لا توجد مبيعات بعد.</p>}
        </section>
      </div>
      <section className="panel">
        <h3>تنبيه المخزون المنخفض <small className="muted">({low.length})</small></h3>
        {low.length ? low.map(p => <div key={p.id} className="row"><span>{p.name}</span><span className={p.stock ? 'amber' : 'red'}>{p.stock} قطعة</span></div>) : <p className="muted">كل المنتجات متوفرة بكميات كافية.</p>}
        <button className="btn outline" onClick={() => go('products')}>إدارة المنتجات</button>
      </section>
    </div>
  );
}

const blank = (): Product => ({
  id: 'p' + Date.now(), name: '', category: 'men', price: 0, description: '', images: [], icon: 'bag', hue: 30,
  rating: 5, reviews: 0, stock: 10, sizes: [], colors: [], createdAt: Date.now(), isNew: true
});

function resize(file: File): Promise<string> {
  return new Promise((res, rej) => {
    const img = new Image();
    img.onload = () => {
      const s = Math.min(1, 900 / Math.max(img.width, img.height));
      const c = document.createElement('canvas');
      c.width = img.width * s; c.height = img.height * s;
      c.getContext('2d')!.drawImage(img, 0, 0, c.width, c.height);
      res(c.toDataURL('image/jpeg', 0.82));
    };
    img.onerror = rej;
    img.src = URL.createObjectURL(file);
  });
}

function Products() {
  const { products, categories, settings, saveProduct, deleteProduct, toast } = useStore();
  const [edit, setEdit] = useState<Product | null>(null);
  const [q, setQ] = useState('');

  if (edit) {
    const set = (k: keyof Product, v: unknown) => setEdit({ ...edit, [k]: v });
    const list = (s: string) => s.split(/[,،]/).map(x => x.trim()).filter(Boolean);
    return (
      <form className="panel stack" onSubmit={e => {
        e.preventDefault();
        const cat = categories.find(c => c.id === edit.category)!;
        saveProduct({ ...edit, icon: cat.icon, hue: edit.images.length ? edit.hue : cat.hue });
        toast('تم حفظ المنتج'); setEdit(null);
      }}>
        <h3>{products.some(p => p.id === edit.id) ? 'تعديل منتج' : 'منتج جديد'}</h3>
        <div className="form-grid">
          <label className="full">اسم المنتج<input required value={edit.name} onChange={e => set('name', e.target.value)} /></label>
          <label>القسم<select value={edit.category} onChange={e => set('category', e.target.value)}>{categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
          <label>المخزون<input type="number" min={0} value={edit.stock} onChange={e => set('stock', +e.target.value)} /></label>
          <label>السعر ({settings.currency})<input type="number" min={0} step="0.01" required value={edit.price} onChange={e => set('price', +e.target.value)} /></label>
          <label>السعر قبل الخصم<input type="number" min={0} step="0.01" value={edit.oldPrice ?? ''} onChange={e => set('oldPrice', e.target.value ? +e.target.value : undefined)} /></label>
          <label className="full">الوصف<textarea rows={3} required value={edit.description} onChange={e => set('description', e.target.value)} /></label>
          <label>المقاسات (مفصولة بفاصلة)<input value={edit.sizes?.join('، ') ?? ''} onChange={e => set('sizes', list(e.target.value))} /></label>
          <label>الألوان (مفصولة بفاصلة)<input value={edit.colors?.join('، ') ?? ''} onChange={e => set('colors', list(e.target.value))} /></label>
          <label className="toggle"><input type="checkbox" checked={!!edit.featured} onChange={e => set('featured', e.target.checked)} /> منتج مميز (الأكثر مبيعاً)</label>
          <label className="toggle"><input type="checkbox" checked={!!edit.isNew} onChange={e => set('isNew', e.target.checked)} /> وصل حديثاً</label>
          <div className="full">
            <span className="label">الصور</span>
            <div className="uploads">
              {edit.images.map((s, i) => <div key={i} className="up"><img src={s} alt="" /><button type="button" onClick={() => set('images', edit.images.filter((_, j) => j !== i))}><Icon name="close" size={14} /></button></div>)}
              <label className="up add"><Icon name="image" /><small>رفع صورة</small>
                <input type="file" accept="image/*" multiple hidden onChange={async e => {
                  const files = [...(e.target.files ?? [])];
                  const urls = await Promise.all(files.map(resize));
                  set('images', [...edit.images, ...urls]);
                }} /></label>
            </div>
            <input placeholder="أو الصق رابط صورة ثم Enter" onKeyDown={e => {
              if (e.key === 'Enter') { e.preventDefault(); const v = e.currentTarget.value.trim(); if (v) { set('images', [...edit.images, v]); e.currentTarget.value = ''; } }
            }} />
          </div>
        </div>
        <div className="hero-cta"><button className="btn primary">حفظ المنتج</button><button type="button" className="btn outline" onClick={() => setEdit(null)}>إلغاء</button></div>
      </form>
    );
  }

  const list = products.filter(p => p.name.includes(q));
  return (
    <section className="panel">
      <div className="toolbar">
        <div className="shop-search grow"><Icon name="search" size={18} /><input value={q} onChange={e => setQ(e.target.value)} placeholder="بحث في المنتجات" /></div>
        <button className="btn primary" onClick={() => setEdit(blank())}><Icon name="plus" size={18} /> منتج جديد</button>
      </div>
      <div className="table">
        {list.map(p => (
          <div key={p.id} className="trow">
            <img src={imageOf(p)} alt="" />
            <div className="grow"><b>{p.name}</b><small className="muted">{categories.find(c => c.id === p.category)?.name}</small></div>
            <span>{money(p.price, settings.currency)}</span>
            <span className={p.stock < 10 ? 'amber' : ''}>{p.stock} قطعة</span>
            <div className="row-actions">
              <button className="icon-btn" onClick={() => setEdit(p)} aria-label="تعديل"><Icon name="edit" size={16} /></button>
              <button className="icon-btn danger" onClick={() => { if (confirm(`حذف "${p.name}"؟`)) { deleteProduct(p.id); toast('تم حذف المنتج'); } }} aria-label="حذف"><Icon name="trash" size={16} /></button>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function Orders() {
  const { orders, settings, setOrderStatus, toast } = useStore();
  const [filter, setFilter] = useState<OrderStatus | ''>('');
  const [open, setOpen] = useState<string>();
  const list = orders.filter(o => !filter || o.status === filter);
  return (
    <section className="panel">
      <div className="chips">
        <button className={!filter ? 'chip on' : 'chip'} onClick={() => setFilter('')}>الكل ({orders.length})</button>
        {(Object.keys(STATUS) as OrderStatus[]).map(s => <button key={s} className={filter === s ? 'chip on' : 'chip'} onClick={() => setFilter(s)}>{STATUS[s].label} ({orders.filter(o => o.status === s).length})</button>)}
      </div>
      {!list.length && <p className="muted">لا توجد طلبات.</p>}
      {list.map(o => (
        <div key={o.id} className="order-card">
          <div className="order-row" onClick={() => setOpen(open === o.id ? undefined : o.id)}>
            <div><b dir="ltr">#{o.id}</b><small className="muted">{o.customer.name} · {date(o.createdAt)}</small></div>
            <span className={`pill ${STATUS[o.status].tone}`}>{STATUS[o.status].label}</span>
            <strong>{money(o.total, settings.currency)}</strong>
          </div>
          {open === o.id && (
            <div className="order-detail">
              <p><b>العميل:</b> {o.customer.name} · <a href={`tel:${o.customer.phone}`} dir="ltr">{o.customer.phone}</a><br /><b>العنوان:</b> {o.customer.city} — {o.customer.address}{o.customer.notes && <><br /><b>ملاحظات:</b> {o.customer.notes}</>}<br /><b>الدفع:</b> {o.payment === 'cod' ? 'عند الاستلام' : 'بطاقة'}{o.coupon && ` · كوبون ${o.coupon}`}</p>
              {o.items.map((i, k) => <div key={k} className="row"><span>{i.name} <small className="muted">{[i.color, i.size].filter(Boolean).join(' · ')} × {i.qty}</small></span><span>{money(i.price * i.qty, settings.currency)}</span></div>)}
              <label className="select">تغيير الحالة:
                <select value={o.status} onChange={e => { setOrderStatus(o.id, e.target.value as OrderStatus); toast('تم تحديث حالة الطلب'); }}>
                  {(Object.keys(STATUS) as OrderStatus[]).map(s => <option key={s} value={s}>{STATUS[s].label}</option>)}
                </select>
              </label>
            </div>
          )}
        </div>
      ))}
    </section>
  );
}

function Coupons() {
  const { coupons, saveCoupon, deleteCoupon, settings, toast } = useStore();
  const [c, setC] = useState<Coupon>({ code: '', type: 'percent', value: 10, active: true });
  return (
    <section className="panel stack">
      <form className="form-grid" onSubmit={e => { e.preventDefault(); saveCoupon({ ...c, code: c.code.trim().toUpperCase() }); toast('تم حفظ الكوبون'); setC({ ...c, code: '' }); }}>
        <label>الكود<input required dir="ltr" value={c.code} onChange={e => setC({ ...c, code: e.target.value })} /></label>
        <label>النوع<select value={c.type} onChange={e => setC({ ...c, type: e.target.value as Coupon['type'] })}><option value="percent">نسبة %</option><option value="fixed">مبلغ ثابت</option></select></label>
        <label>القيمة<input type="number" min={1} required value={c.value} onChange={e => setC({ ...c, value: +e.target.value })} /></label>
        <button className="btn primary">إضافة كوبون</button>
      </form>
      {coupons.map(x => (
        <div key={x.code} className="trow">
          <b dir="ltr" className="grow">{x.code}</b>
          <span>{x.type === 'percent' ? `${x.value}%` : money(x.value, settings.currency)}</span>
          <label className="toggle"><input type="checkbox" checked={x.active} onChange={e => saveCoupon({ ...x, active: e.target.checked })} /> مفعّل</label>
          <button className="icon-btn danger" onClick={() => deleteCoupon(x.code)} aria-label="حذف"><Icon name="trash" size={16} /></button>
        </div>
      ))}
    </section>
  );
}

function SettingsTab() {
  const { settings, saveSettings, resetStore, toast } = useStore();
  const [s, setS] = useState<Settings>(settings);
  const f = (k: keyof Settings, num = false) => ({
    value: s[k], onChange: (e: React.ChangeEvent<HTMLInputElement>) => setS({ ...s, [k]: num ? +e.target.value : e.target.value })
  });
  return (
    <form className="panel stack" onSubmit={e => { e.preventDefault(); saveSettings(s); toast('تم حفظ الإعدادات'); }}>
      <div className="form-grid">
        <label>العملة<input {...f('currency')} /></label>
        <label>رسوم الشحن<input type="number" min={0} {...f('shippingFee', true)} /></label>
        <label>شحن مجاني فوق<input type="number" min={0} {...f('freeShippingOver', true)} /></label>
        <label>رقم واتساب (بالصيغة الدولية)<input dir="ltr" placeholder="9665xxxxxxxx" {...f('whatsapp')} /></label>
        <label className="full">البريد الإلكتروني للدعم<input dir="ltr" {...f('email')} /></label>
        <label className="full">شريط الإعلانات<input {...f('announcement')} /></label>
      </div>
      <div className="hero-cta">
        <button className="btn primary">حفظ الإعدادات</button>
        <button type="button" className="btn outline danger" onClick={() => { if (confirm('إعادة المتجر لبياناته الافتراضية؟ سيتم حذف الطلبات والتعديلات.')) { resetStore(); toast('تمت إعادة الضبط'); } }}>إعادة ضبط المتجر</button>
      </div>
    </form>
  );
}
