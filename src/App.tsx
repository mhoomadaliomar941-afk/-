import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { StoreProvider } from './store/StoreContext';
import Layout from './components/Layout';
import Home from './pages/Home';
import Shop from './pages/Shop';
import ProductPage from './pages/ProductPage';
import Cart from './pages/Cart';
import Checkout from './pages/Checkout';
import OrderPage from './pages/OrderPage';
import Wishlist from './pages/Wishlist';
import Account from './pages/Account';
import Info from './pages/Info';
import Admin from './pages/admin/Admin';

export default function App() {
  return (
    <StoreProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<Home />} />
            <Route path="shop" element={<Shop />} />
            <Route path="product/:id" element={<ProductPage />} />
            <Route path="cart" element={<Cart />} />
            <Route path="checkout" element={<Checkout />} />
            <Route path="order/:id" element={<OrderPage />} />
            <Route path="wishlist" element={<Wishlist />} />
            <Route path="account" element={<Account />} />
            <Route path="info/:slug" element={<Info />} />
            <Route path="admin" element={<Admin />} />
            <Route path="*" element={<Info />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </StoreProvider>
  );
}
