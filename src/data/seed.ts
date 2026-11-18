import type { Category, Coupon, Product, Settings, User } from '../lib/types';

export const CATEGORIES: Category[] = [
  { id: 'men', name: 'أزياء رجالية', icon: 'shirt', hue: 215 },
  { id: 'women', name: 'أزياء نسائية', icon: 'dress', hue: 340 },
  { id: 'electronics', name: 'إلكترونيات', icon: 'headphones', hue: 200 },
  { id: 'watches', name: 'ساعات', icon: 'watch', hue: 38 },
  { id: 'perfumes', name: 'عطور', icon: 'perfume', hue: 280 },
  { id: 'shoes', name: 'أحذية', icon: 'sneaker', hue: 160 },
  { id: 'bags', name: 'حقائب', icon: 'bag', hue: 20 },
  { id: 'home', name: 'المنزل', icon: 'lamp', hue: 50 }
];

const now = Date.now();
const day = 86400000;
let n = 0;
const p = (x: Omit<Product, 'id' | 'images' | 'createdAt' | 'hue' | 'icon'> & Partial<Product>): Product => {
  const cat = CATEGORIES.find(c => c.id === x.category)!;
  n++;
  return { id: 'p' + n, images: [], createdAt: now - n * day, icon: cat.icon, hue: cat.hue + ((n * 17) % 30) - 15, ...x };
};

const SIZES = ['S', 'M', 'L', 'XL', 'XXL'];
const SHOE = ['40', '41', '42', '43', '44', '45'];

export const PRODUCTS: Product[] = [
  p({ name: 'قميص أكسفورد كلاسيكي', category: 'men', price: 129, oldPrice: 179, description: 'قميص قطني 100% بقصّة عصرية مريحة، مثالي للعمل والمناسبات. قماش ناعم يسمح بمرور الهواء ولا يتجعّد بسهولة.', rating: 4.7, reviews: 214, stock: 40, sizes: SIZES, colors: ['أبيض', 'سماوي', 'كحلي'], featured: true }),
  p({ name: 'جاكيت جينز عصري', category: 'men', price: 249, description: 'جاكيت جينز متين بغسلة فاتحة وأزرار معدنية، يمنحك إطلالة كاجوال أنيقة طوال العام.', rating: 4.5, reviews: 98, stock: 22, sizes: SIZES, colors: ['أزرق فاتح', 'أسود'], isNew: true }),
  p({ name: 'تيشيرت بيسك قطني', category: 'men', price: 59, oldPrice: 79, description: 'تيشيرت أساسي من القطن المصري بخامة ثقيلة وياقة دائرية لا تفقد شكلها بعد الغسيل.', rating: 4.8, reviews: 530, stock: 120, sizes: SIZES, colors: ['أسود', 'أبيض', 'رمادي', 'زيتي'] }),
  p({ name: 'فستان سهرة ساتان', category: 'women', price: 389, oldPrice: 459, description: 'فستان سهرة من الساتان الفاخر بقصّة انسيابية وتفاصيل راقية عند الخصر.', rating: 4.9, reviews: 76, stock: 12, sizes: ['S', 'M', 'L'], colors: ['زمردي', 'نبيتي', 'أسود'], featured: true }),
  p({ name: 'عباية كريب مطرزة', category: 'women', price: 299, description: 'عباية كريب خفيفة بتطريز يدوي ناعم على الأكمام، تجمع بين الحشمة والأناقة.', rating: 4.8, reviews: 142, stock: 30, sizes: ['52', '54', '56', '58'], colors: ['أسود', 'بيج'], isNew: true }),
  p({ name: 'بلوزة كتان صيفية', category: 'women', price: 139, description: 'بلوزة من الكتان الطبيعي بأكمام واسعة، مريحة وأنيقة للأيام الحارة.', rating: 4.4, reviews: 61, stock: 35, sizes: ['S', 'M', 'L', 'XL'], colors: ['أبيض', 'وردي', 'ليموني'] }),
  p({ name: 'سماعات لاسلكية Pro', category: 'electronics', price: 499, oldPrice: 649, description: 'سماعات بخاصية إلغاء الضوضاء النشط، بطارية حتى 40 ساعة، وصوت محيطي غامر مع ميكروفون للمكالمات.', rating: 4.8, reviews: 1203, stock: 50, colors: ['أسود', 'فضي'], featured: true }),
  p({ name: 'سماعات أذن رياضية', category: 'electronics', price: 199, description: 'سماعات أذن مقاومة للماء والعرق مع علبة شحن سريع وتوصيل بلوتوث 5.3.', rating: 4.5, reviews: 640, stock: 80, colors: ['أبيض', 'أسود'], isNew: true, icon: 'headphones' }),
  p({ name: 'هاتف ذكي ZX 5G', category: 'electronics', price: 1899, oldPrice: 2199, description: 'شاشة AMOLED مقاس 6.7 إنش، كاميرا 108 ميجابكسل، ذاكرة 256GB وشحن سريع 67 واط.', rating: 4.6, reviews: 389, stock: 15, colors: ['أسود', 'أزرق', 'ذهبي'], featured: true, icon: 'phone' }),
  p({ name: 'ساعة ذكية Fit', category: 'electronics', price: 349, description: 'تتبّع النبض والنوم والأكسجين، أكثر من 100 وضع رياضي، وبطارية تدوم 10 أيام.', rating: 4.4, reviews: 512, stock: 60, colors: ['أسود', 'وردي', 'أخضر'], icon: 'watch' }),
  p({ name: 'ساعة كرونوغراف فولاذية', category: 'watches', price: 899, oldPrice: 1099, description: 'ساعة رجالية بهيكل فولاذي مقاوم للصدأ، زجاج سافاير، ومقاومة للماء حتى 100 متر.', rating: 4.9, reviews: 187, stock: 10, colors: ['فضي', 'ذهبي', 'أسود'], featured: true }),
  p({ name: 'ساعة نسائية ناعمة', category: 'watches', price: 459, description: 'ساعة أنيقة بسوار شبكي وقرص صدفي لامع تضيف لمسة رقيّ لإطلالتك اليومية.', rating: 4.7, reviews: 95, stock: 18, colors: ['ذهبي وردي', 'فضي'], isNew: true }),
  p({ name: 'نظارة شمسية كلاسيك', category: 'watches', price: 219, description: 'عدسات مستقطبة بحماية UV400 كاملة وإطار خفيف متين بتصميم خالد.', rating: 4.6, reviews: 154, stock: 45, colors: ['أسود', 'هافانا'], icon: 'glasses' }),
  p({ name: 'عطر عود ملكي', category: 'perfumes', price: 420, oldPrice: 520, description: 'مزيج شرقي فاخر من العود الكمبودي والعنبر والورد الطائفي، ثبات يدوم طويلاً. 100 مل.', rating: 4.9, reviews: 860, stock: 25, featured: true }),
  p({ name: 'عطر زهور الربيع', category: 'perfumes', price: 310, description: 'عطر نسائي منعش بنفحات الياسمين والفاوانيا والمسك الأبيض. 75 مل.', rating: 4.7, reviews: 302, stock: 33, isNew: true }),
  p({ name: 'مسك الطهارة الفاخر', category: 'perfumes', price: 89, description: 'مسك أبيض نقي بتركيز عالٍ وثبات ممتاز، مناسب للاستخدام اليومي. 30 مل.', rating: 4.8, reviews: 1500, stock: 200 }),
  p({ name: 'حذاء رياضي Air Run', category: 'shoes', price: 349, oldPrice: 429, description: 'نعل مرن يمتص الصدمات وجزء علوي شبكي يسمح بالتهوية، مثالي للجري والمشي الطويل.', rating: 4.7, reviews: 720, stock: 40, sizes: SHOE, colors: ['أبيض', 'أسود', 'رمادي'], featured: true }),
  p({ name: 'حذاء جلد رسمي', category: 'shoes', price: 399, description: 'حذاء من الجلد الطبيعي مصنوع يدوياً بنعل مريح، الخيار الأمثل للمناسبات الرسمية.', rating: 4.6, reviews: 133, stock: 20, sizes: SHOE, colors: ['أسود', 'بني'] }),
  p({ name: 'سنيكرز نسائي', category: 'shoes', price: 279, description: 'سنيكرز خفيف بتصميم عصري ونعل مريح لليوم الطويل.', rating: 4.5, reviews: 210, stock: 38, sizes: ['36', '37', '38', '39', '40'], colors: ['أبيض', 'وردي'], isNew: true }),
  p({ name: 'حقيبة يد جلدية', category: 'bags', price: 459, oldPrice: 549, description: 'حقيبة يد من الجلد الطبيعي بتقسيمات داخلية عملية وحزام كتف قابل للفصل.', rating: 4.8, reviews: 176, stock: 14, colors: ['كاميل', 'أسود', 'عنابي'], featured: true }),
  p({ name: 'حقيبة ظهر للابتوب', category: 'bags', price: 229, description: 'حقيبة ظهر مقاومة للماء مع جيب مبطّن للابتوب حتى 16 إنش ومنفذ USB للشحن.', rating: 4.7, reviews: 488, stock: 55, colors: ['أسود', 'رمادي', 'كحلي'] }),
  p({ name: 'محفظة رجالية نحيفة', category: 'bags', price: 99, description: 'محفظة جلدية نحيفة بحماية RFID تتسع لـ 8 بطاقات.', rating: 4.6, reviews: 341, stock: 90, colors: ['أسود', 'بني'], isNew: true }),
  p({ name: 'أباجورة ليد ذكية', category: 'home', price: 189, description: 'إضاءة ليد بقابلية تعديل الشدة ودرجة اللون، مع تحكم باللمس وشاحن لاسلكي مدمج.', rating: 4.5, reviews: 120, stock: 28, colors: ['أبيض', 'أسود'] }),
  p({ name: 'فوّاحة عطرية خشبية', category: 'home', price: 149, oldPrice: 189, description: 'فوّاحة بخار بتصميم خشبي وإضاءة هادئة بـ 7 ألوان، سعة 300 مل.', rating: 4.6, reviews: 265, stock: 42, isNew: true })
];

export const COUPONS: Coupon[] = [
  { code: 'ZENO10', type: 'percent', value: 10, active: true },
  { code: 'WELCOME50', type: 'fixed', value: 50, active: true }
];

export const SETTINGS: Settings = {
  storeName: 'ZENO',
  currency: 'ر.س',
  shippingFee: 25,
  freeShippingOver: 300,
  whatsapp: '',
  email: 'support@zeno.store',
  announcement: 'شحن مجاني للطلبات فوق 300 ر.س · استخدم كود ZENO10 لخصم 10%'
};

export const ADMIN: User = { name: 'مدير المتجر', email: 'admin@zeno.store', password: 'zeno1234', isAdmin: true };
