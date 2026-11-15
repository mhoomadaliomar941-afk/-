import { test, expect, btn, login, tab, visible } from './fixtures';

const ADMIN = '790000000';

test('F07-N1 a member has no admin panel', async ({ page }) => {
  await login(page);
  await tab(page, 'حسابي').click();
  await expect(page.getByText('تسجيل الخروج')).toBeVisible();
  await expect(btn(page, 'لوحة الإدارة')).toHaveCount(0);
});

test('F07-H1 admin creates a draw product, opens it, members see it', async ({ page, a11y }) => {
  page.on('dialog', (d) => d.accept());
  await login(page, ADMIN);
  await tab(page, 'حسابي').click();
  await btn(page, 'لوحة الإدارة').click();
  await a11y('admin home');
  await btn(page, 'منتجات السحب').click();
  await btn(page, 'منتج جديد').click();
  await a11y('admin product form');
  await btn(page, 'حفظ').click();
  await expect(page.getByText('تأكد من الاسم والأسعار وعدد الأماكن')).toBeVisible();
  await page.getByLabel('اسم المنتج').fill('مكنسة روبوت');
  await page.getByRole('radio', { name: 'للبيت' }).count().then((n) => expect(n).toBe(0)); // draw categories only
  await page.getByLabel('سعره بالسوق (د.ا)').fill('180');
  await page.getByLabel('سعر المكان (د.ا)').fill('1');
  await page.getByLabel('عدد الأماكن').fill('900');
  await expect(page.getByText(/مجموع الأماكن/)).toContainText('900.000');
  await btn(page, 'حفظ').click();
  await expect(visible(page, 'button', /مكنسة روبوت/)).toBeVisible();
  await visible(page, 'button', /مكنسة روبوت/).click();
  await expect(page.getByText('مسودة').last()).toBeVisible();
  await btn(page, 'افتح المنتج للحجز').click();
  await expect(visible(page, 'button', /مكنسة روبوت/)).toBeVisible();
  await page.goBack(); await page.goBack(); await page.goBack();
  await tab(page, 'الرئيسية').click();
  await expect(visible(page, 'button', /مكنسة روبوت/)).toBeVisible();
});

test('F07-H2 admin adds a store product; hiding it removes it from the store', async ({ page, a11y }) => {
  await login(page, ADMIN);
  await tab(page, 'حسابي').click();
  await btn(page, 'لوحة الإدارة').click();
  await btn(page, 'منتجات المتجر').click();
  await btn(page, 'منتج جديد').click();
  await page.getByLabel('اسم المنتج').fill('حامل جوال للسيارة');
  await page.getByRole('radio', { name: 'غير ذلك' }).click();
  await page.getByLabel('السعر (د.ا)').fill('3.5');
  await page.getByLabel('الكمية بالمخزن').fill('10');
  await a11y('admin store product form');
  await btn(page, 'حفظ').click();
  await expect(visible(page, 'button', /حامل جوال/)).toBeVisible();
  await page.goBack(); await page.goBack();
  await tab(page, 'المتجر').click();
  await expect(visible(page, 'button', /حامل جوال/)).toBeVisible();
  await expect(visible(page, 'button', /حامل جوال/)).toContainText('3.500');
  // hide it
  await tab(page, 'حسابي').click();
  await btn(page, 'لوحة الإدارة').click();
  await btn(page, 'منتجات المتجر').click();
  await visible(page, 'button', /حامل جوال/).click();
  await page.getByRole('switch', { name: 'ظاهر بالمتجر' }).click();
  await btn(page, 'حفظ').click();
  await expect(page.getByText('انحفظ')).toBeVisible();
  await page.goBack(); await page.goBack(); await page.goBack();
  await tab(page, 'المتجر').click();
  await expect(page.getByRole('button', { name: /حامل جوال/ }).locator('visible=true')).toHaveCount(0);
});

test('F07-H3 admin ships a store order; cancelling refunds the buyer', async ({ page, a11y }) => {
  await login(page, ADMIN); // the demo admin is also a member with the demo balances
  await tab(page, 'المتجر').click();
  await visible(page, 'button', /كفر جوال/).click();
  await page.getByRole('button', { name: /^اشترِ ·/ }).click();
  await page.getByLabel('المحافظة').fill('عمّان'); await page.getByLabel('المدينة').fill('عمّان');
  await page.getByLabel('الشارع والبناية').fill('شارع ٣'); await page.getByLabel('رقم للتواصل').fill('0791234567');
  await btn(page, 'احفظ العنوان').click();
  await btn(page, 'أكّد الشراء').click();
  await expect(page.getByText('قيد التجهيز')).toBeVisible();
  await page.goBack(); await page.goBack();
  await tab(page, 'حسابي').click();
  await btn(page, 'لوحة الإدارة').click();
  await btn(page, /^الطلبات/).click();
  await page.getByRole('tab', { name: 'متجر' }).click();
  await a11y('admin orders');
  await expect(page.getByText(/كفر جوال شفاف ×/).last()).toBeVisible();
  await expect(page.getByText(/شارع ٣/)).toBeVisible();
  await btn(page, 'علّم إنه انشحن').click();
  await expect(page.getByText('بالطريق').last()).toBeVisible();
  await btn(page, 'ألغِ الطلب').click();
  await expect(page.getByText('ملغي').last()).toBeVisible();
  await page.goBack(); await page.goBack();
  await tab(page, 'المحفظة').click();
  await expect(page.getByText('رصيد التسوق', { exact: true }).locator('xpath=..')).toContainText('5.000');
});
