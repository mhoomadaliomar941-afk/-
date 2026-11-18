import { Link } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import ProductCard from '../components/ProductCard';
import Icon from '../components/Icon';
import { productArt } from '../lib/art';

export default function Home() {
  const { products, categories, settings } = useStore();
  const featured = products.filter(p => p.featured).slice(0, 8);
  const fresh = [...products].sort((a, b) => b.createdAt - a.createdAt).filter(p => p.isNew).slice(0, 8);
  const sale = products.filter(p => p.oldPrice && p.oldPrice > p.price).slice(0, 4);

  return (
    <>
      <section className="hero">
        <div className="container hero-inner">
          <div className="hero-copy">
            <span className="eyebrow">مجموعة الموسم الجديد</span>
            <h1>أناقتك تبدأ من <em>زينو</em></h1>
            <p>أحدث صيحات الأزياء والإلكترونيات والعطور الفاخرة، مختارة بعناية وتصلك أينما كنت.</p>
            <div className="hero-cta">
              <Link to="/shop" className="btn primary">تسوّق الآن</Link>
              <Link to="/shop?sale=1" className="btn outline">اكتشف العروض</Link>
            </div>
            <div className="hero-stats">
              <div><strong>+50K</strong><span>عميل سعيد</span></div>
              <div><strong>+{products.length * 40}</strong><span>منتج أصلي</span></div>
              <div><strong>4.8★</strong><span>تقييم العملاء</span></div>
            </div>
          </div>
          <div className="hero-art">
            <div className="hero-ring" />
            <div className="hero-tile t1"><img src={productArt('watch', 38)} alt="" /></div>
            <div className="hero-tile t2"><img src={productArt('headphones', 205)} alt="" /></div>
            <div className="hero-tile t3"><img src={productArt('perfume', 285)} alt="" /></div>
          </div>
        </div>
      </section>

      <section className="container section">
        <div className="section-head"><h2>تسوّق حسب القسم</h2><Link to="/shop">عرض الكل</Link></div>
        <div className="cats">
          {categories.map(c => (
            <Link key={c.id} to={`/shop?cat=${c.id}`} className="cat">
              <span className="cat-img"><img src={productArt(c.icon, c.hue)} alt="" /></span>
              <span>{c.name}</span>
            </Link>
          ))}
        </div>
      </section>

      <section className="container section">
        <div className="section-head"><h2>الأكثر مبيعاً</h2><Link to="/shop?sort=popular">عرض الكل</Link></div>
        <div className="grid">{featured.map(p => <ProductCard key={p.id} p={p} />)}</div>
      </section>

      <section className="container section">
        <div className="promo">
          <div>
            <span className="eyebrow">عرض لفترة محدودة</span>
            <h2>خصم 10% على طلبك الأول</h2>
            <p>استخدم الكود <code>ZENO10</code> عند إتمام الطلب، وشحن مجاني للطلبات فوق {settings.freeShippingOver} {settings.currency}.</p>
            <Link to="/shop" className="btn light">ابدأ التسوّق</Link>
          </div>
          <div className="promo-grid">{sale.map(p => <Link key={p.id} to={`/product/${p.id}`}><img src={productArt(p.icon, p.hue)} alt={p.name} /></Link>)}</div>
        </div>
      </section>

      {fresh.length > 0 && (
        <section className="container section">
          <div className="section-head"><h2>وصل حديثاً</h2><Link to="/shop?sort=new">عرض الكل</Link></div>
          <div className="grid">{fresh.map(p => <ProductCard key={p.id} p={p} />)}</div>
        </section>
      )}

      <section className="container section features">
        {[
          ['truck', 'توصيل سريع', 'خلال 1-3 أيام عمل'],
          ['shield', 'دفع آمن', 'بطاقات أو الدفع عند الاستلام'],
          ['refresh', 'استرجاع سهل', 'خلال 14 يوماً من الاستلام'],
          ['headset', 'دعم متواصل', 'فريقنا جاهز لمساعدتك']
        ].map(([i, t, d]) => (
          <div key={t} className="feature"><span><Icon name={i} /></span><div><strong>{t}</strong><small>{d}</small></div></div>
        ))}
      </section>
    </>
  );
}
