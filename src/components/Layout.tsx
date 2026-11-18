import { useEffect, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import Icon from './Icon';
import Logo from './Logo';

export default function Layout() {
  const { cartCount, wishlist, settings, categories, toasts, user } = useStore();
  const [q, setQ] = useState('');
  const [menu, setMenu] = useState(false);
  const nav = useNavigate();
  const loc = useLocation();

  useEffect(() => { window.scrollTo(0, 0); setMenu(false); }, [loc.pathname]);

  const search = (e: React.FormEvent) => {
    e.preventDefault();
    nav(`/shop?q=${encodeURIComponent(q.trim())}`);
  };

  return (
    <div className="app">
      {settings.announcement && <div className="announce">{settings.announcement}</div>}
      <header className="header">
        <div className="container header-row">
          <button className="icon-btn ghost only-mobile" onClick={() => setMenu(true)} aria-label="القائمة"><Icon name="menu" /></button>
          <Logo />
          <nav className="main-nav only-desktop">
            <NavLink to="/" end>الرئيسية</NavLink>
            <NavLink to="/shop" end>المتجر</NavLink>
            <Link to="/shop?sort=new">وصل حديثاً</Link>
            <Link to="/shop?sale=1">العروض</Link>
            <NavLink to="/info/contact">تواصل معنا</NavLink>
          </nav>
          <form className="search only-desktop" onSubmit={search}>
            <Icon name="search" size={18} />
            <input value={q} onChange={e => setQ(e.target.value)} placeholder="ابحث عن منتج..." />
          </form>
          <div className="header-actions">
            <Link to="/shop" className="icon-btn ghost only-mobile" aria-label="بحث"><Icon name="search" /></Link>
            <Link to="/wishlist" className="icon-btn ghost only-desktop" aria-label="المفضلة">
              <Icon name="heart" />{wishlist.length > 0 && <span className="dot">{wishlist.length}</span>}
            </Link>
            <Link to="/account" className="icon-btn ghost only-desktop" aria-label="حسابي"><Icon name="user" /></Link>
            <Link to="/cart" className="icon-btn ghost" aria-label="السلة">
              <Icon name="cart" />{cartCount > 0 && <span className="dot">{cartCount}</span>}
            </Link>
          </div>
        </div>
      </header>

      {menu && (
        <div className="drawer-backdrop" onClick={() => setMenu(false)}>
          <aside className="drawer" onClick={e => e.stopPropagation()}>
            <div className="drawer-head"><Logo /><button className="icon-btn ghost" onClick={() => setMenu(false)}><Icon name="close" /></button></div>
            <Link to="/shop?sort=new">وصل حديثاً</Link>
            <Link to="/shop?sale=1">العروض والخصومات</Link>
            <h4>الأقسام</h4>
            {categories.map(c => <Link key={c.id} to={`/shop?cat=${c.id}`}>{c.name}</Link>)}
            <h4>المساعدة</h4>
            <Link to="/info/shipping">الشحن والتوصيل</Link>
            <Link to="/info/returns">الاستبدال والاسترجاع</Link>
            <Link to="/info/contact">تواصل معنا</Link>
            {user?.isAdmin && <Link to="/admin" className="accent">لوحة التحكم</Link>}
          </aside>
        </div>
      )}

      <main className="main"><Outlet /></main>

      <footer className="footer">
        <div className="container footer-grid">
          <div>
            <Logo />
            <p className="muted">زينو — وجهتك للأناقة والتقنية. منتجات أصلية، توصيل سريع، ودفع آمن.</p>
          </div>
          <div>
            <h4>تسوّق</h4>
            {categories.slice(0, 5).map(c => <Link key={c.id} to={`/shop?cat=${c.id}`}>{c.name}</Link>)}
          </div>
          <div>
            <h4>المساعدة</h4>
            <Link to="/info/shipping">الشحن والتوصيل</Link>
            <Link to="/info/returns">الاستبدال والاسترجاع</Link>
            <Link to="/info/privacy">سياسة الخصوصية</Link>
            <Link to="/info/terms">الشروط والأحكام</Link>
          </div>
          <div>
            <h4>تواصل معنا</h4>
            <a href={`mailto:${settings.email}`}>{settings.email}</a>
            {settings.whatsapp && <a href={`https://wa.me/${settings.whatsapp}`} target="_blank" rel="noreferrer">واتساب</a>}
            <Link to="/account">حسابي</Link>
          </div>
        </div>
        <div className="container copy">© {new Date().getFullYear()} ZENO زينو. جميع الحقوق محفوظة.</div>
      </footer>

      <nav className="bottom-nav only-mobile">
        <NavLink to="/" end><Icon name="home" /><span>الرئيسية</span></NavLink>
        <NavLink to="/shop"><Icon name="grid" /><span>المتجر</span></NavLink>
        <NavLink to="/cart"><Icon name="cart" />{cartCount > 0 && <i className="dot">{cartCount}</i>}<span>السلة</span></NavLink>
        <NavLink to="/wishlist"><Icon name="heart" /><span>المفضلة</span></NavLink>
        <NavLink to="/account"><Icon name="user" /><span>حسابي</span></NavLink>
      </nav>

      <div className="toasts">{toasts.map(t => <div key={t.id} className="toast"><Icon name="check" size={18} />{t.text}</div>)}</div>
    </div>
  );
}
