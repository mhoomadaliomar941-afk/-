import { Link } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import ProductCard from '../components/ProductCard';
import Icon from '../components/Icon';

export default function Wishlist() {
  const { wishlist, getProduct } = useStore();
  const items = wishlist.map(getProduct).filter(Boolean);
  return (
    <div className="container page">
      <div className="page-head"><h1>المفضلة</h1><span className="muted">{items.length} منتج</span></div>
      {items.length ? <div className="grid">{items.map(p => <ProductCard key={p!.id} p={p!} />)}</div> : (
        <div className="empty"><Icon name="heart" size={48} /><h3>قائمة المفضلة فارغة</h3><p>اضغط على ♡ في أي منتج لحفظه هنا.</p><Link to="/shop" className="btn primary">تصفّح المنتجات</Link></div>
      )}
    </div>
  );
}
