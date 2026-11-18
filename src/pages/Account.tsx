import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useStore } from '../store/StoreContext';
import { STATUS, date, money } from '../lib/format';
import Icon from '../components/Icon';

export default function Account() {
  const { user, login, register, logout, orders, settings, updateProfile, toast } = useStore();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [form, setForm] = useState({ name: '', email: '', phone: '', password: '' });
  const [err, setErr] = useState('');

  if (!user) {
    const submit = (e: React.FormEvent) => {
      e.preventDefault();
      if (mode === 'register' && form.password.length < 6) return setErr('كلمة المرور يجب أن تكون 6 أحرف على الأقل');
      setErr((mode === 'login' ? login(form.email, form.password) : register(form)) ?? '');
    };
    const f = (k: keyof typeof form) => ({ value: form[k], onChange: (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value }) });
    return (
      <div className="container page auth">
        <div className="panel">
          <div className="tabs">
            <button className={mode === 'login' ? 'on' : ''} onClick={() => { setMode('login'); setErr(''); }}>تسجيل الدخول</button>
            <button className={mode === 'register' ? 'on' : ''} onClick={() => { setMode('register'); setErr(''); }}>حساب جديد</button>
          </div>
          <form className="stack" onSubmit={submit}>
            {mode === 'register' && <label>الاسم<input required {...f('name')} /></label>}
            <label>البريد الإلكتروني<input required type="email" dir="ltr" {...f('email')} /></label>
            {mode === 'register' && <label>رقم الجوال<input type="tel" dir="ltr" {...f('phone')} /></label>}
            <label>كلمة المرور<input required type="password" dir="ltr" {...f('password')} /></label>
            {err && <div className="alert">{err}</div>}
            <button className="btn primary block">{mode === 'login' ? 'دخول' : 'إنشاء الحساب'}</button>
          </form>
        </div>
      </div>
    );
  }

  const mine = orders.filter(o => o.userEmail === user.email);
  return (
    <div className="container page">
      <div className="profile">
        <span className="avatar">{user.name.slice(0, 1)}</span>
        <div><h1>{user.name}</h1><span className="muted" dir="ltr">{user.email}</span></div>
        <div className="grow" />
        {user.isAdmin && <Link to="/admin" className="btn dark"><Icon name="chart" size={18} /> لوحة التحكم</Link>}
        <button className="btn outline" onClick={logout}><Icon name="logout" size={18} /> خروج</button>
      </div>

      <div className="two-col">
        <section className="panel">
          <h3>طلباتي</h3>
          {mine.length ? mine.map(o => (
            <Link key={o.id} to={`/order/${o.id}`} className="order-row">
              <div><b dir="ltr">#{o.id}</b><small className="muted">{date(o.createdAt)} · {o.items.reduce((a, i) => a + i.qty, 0)} منتج</small></div>
              <span className={`pill ${STATUS[o.status].tone}`}>{STATUS[o.status].label}</span>
              <strong>{money(o.total, settings.currency)}</strong>
            </Link>
          )) : <p className="muted">لا توجد طلبات بعد. <Link to="/shop">ابدأ التسوّق</Link></p>}
        </section>
        <section className="panel">
          <h3>بياناتي</h3>
          <form className="stack" onSubmit={e => {
            e.preventDefault();
            const d = new FormData(e.currentTarget);
            updateProfile({ name: String(d.get('name')), phone: String(d.get('phone')) });
            toast('تم حفظ البيانات');
          }}>
            <label>الاسم<input name="name" defaultValue={user.name} required /></label>
            <label>الجوال<input name="phone" dir="ltr" defaultValue={user.phone} /></label>
            <button className="btn primary">حفظ</button>
          </form>
        </section>
      </div>
    </div>
  );
}
