import { Link } from 'react-router-dom';

export default function Logo() {
  return (
    <Link to="/" className="logo" aria-label="ZENO الرئيسية">
      <span className="logo-mark">Z</span>
      <span className="logo-text">ZENO<small>زينو</small></span>
    </Link>
  );
}
