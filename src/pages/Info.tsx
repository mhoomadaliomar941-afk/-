import { Link, useParams } from 'react-router-dom';
import { useStore } from '../store/StoreContext';

export default function Info() {
  const { slug } = useParams();
  const { settings } = useStore();
  const pages: Record<string, { title: string; body: string[] }> = {
    shipping: { title: 'الشحن والتوصيل', body: [
      `نوصل طلباتك خلال 1-3 أيام عمل داخل المدن الرئيسية، و3-7 أيام لباقي المناطق.`,
      `رسوم الشحن ${settings.shippingFee} ${settings.currency}، والشحن مجاني للطلبات التي تتجاوز ${settings.freeShippingOver} ${settings.currency}.`,
      'ستصلك رسالة برقم الشحنة فور خروج طلبك من المستودع، ويمكنك متابعة حالة الطلب من صفحة "حسابي".'
    ] },
    returns: { title: 'الاستبدال والاسترجاع', body: [
      'يمكنك استبدال أو استرجاع المنتج خلال 14 يوماً من تاريخ الاستلام بشرط أن يكون بحالته الأصلية وبتغليفه.',
      'العطور ومنتجات العناية الشخصية المفتوحة غير قابلة للاسترجاع لأسباب صحية.',
      'يُعاد المبلغ بنفس طريقة الدفع خلال 5-10 أيام عمل من استلامنا للمنتج.'
    ] },
    privacy: { title: 'سياسة الخصوصية', body: [
      'نحترم خصوصيتك. نجمع فقط البيانات اللازمة لتنفيذ طلبك: الاسم، رقم الجوال، العنوان، والبريد الإلكتروني.',
      'لا نبيع بياناتك ولا نشاركها مع أي طرف ثالث إلا شركات الشحن وبوابات الدفع بالقدر اللازم لإتمام الطلب.',
      `لطلب حذف بياناتك أو الاستفسار عنها تواصل معنا على ${settings.email}.`
    ] },
    terms: { title: 'الشروط والأحكام', body: [
      'باستخدامك لمتجر زينو فإنك توافق على هذه الشروط. الأسعار شاملة الضريبة ما لم يُذكر خلاف ذلك.',
      'نحتفظ بحق إلغاء أي طلب في حال وجود خطأ في السعر أو نفاد المخزون، مع إعادة المبلغ كاملاً.',
      'جميع العلامات والمحتوى في المتجر ملك لزينو ولا يجوز استخدامها دون إذن.'
    ] },
    contact: { title: 'تواصل معنا', body: [
      `البريد الإلكتروني: ${settings.email}`,
      settings.whatsapp ? `واتساب: ${settings.whatsapp}` : 'فريق خدمة العملاء متاح يومياً من 9 صباحاً حتى 11 مساءً.',
      'نرد على جميع الرسائل خلال 24 ساعة كحد أقصى.'
    ] }
  };
  const p = pages[slug ?? ''];
  if (!p) return <div className="container page empty"><h3>الصفحة غير موجودة</h3><Link to="/" className="btn primary">الرئيسية</Link></div>;
  return (
    <div className="container page narrow">
      <div className="page-head"><h1>{p.title}</h1></div>
      <section className="panel prose">{p.body.map((t, i) => <p key={i}>{t}</p>)}</section>
    </div>
  );
}
