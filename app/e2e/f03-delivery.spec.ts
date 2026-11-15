import { test, expect, btn, login, tab, visible } from './fixtures';
import type { Page } from '@playwright/test';

async function openOrder(page: Page) {
  await login(page);
  await tab(page, 'مشاركاتي').click();
  await page.getByRole('tab', { name: 'ربحتها' }).click();
  await page.getByRole('button', { name: 'تابلت ١١ إنش للرسم والدراسة', exact: true }).click(); // the card in Mine, not the hidden feed card
  await expect(page.getByText('مبروك! صار المنتج إلك')).toBeVisible();
  await btn(page, 'أدخل عنوان التوصيل').click();
  await expect(page.getByText('بانتظار العنوان')).toBeVisible();
}
const fill = async (page: Page, street: string, phone: string) => {
  await page.getByLabel('المحافظة').fill('عمّان');
  await page.getByLabel('المدينة').fill('عمّان');
  await page.getByLabel('الشارع والبناية').fill(street);
  await page.getByLabel('رقم للتواصل').fill(phone);
};

test.describe('F03 owner receives', () => {
  test('F03-H1 address moves the order to "being prepared"', async ({ page, a11y }) => {
    await openOrder(page);
    await a11y('S10 order');
    await fill(page, 'شارع المدينة المنورة، بناية ١٢', '0791234567');
    await btn(page, 'أكّد العنوان').click();
    await expect(page.getByLabel('المحافظة')).toHaveCount(0);
    await expect(page.getByText('قيد التجهيز')).toBeVisible();
  });

  test('F03-N1 empty form shows 4 required errors', async ({ page }) => {
    await openOrder(page);
    await btn(page, 'أكّد العنوان').click();
    await expect(page.getByText('هذا الحقل مطلوب')).toHaveCount(4);
    await expect(page.getByText('بانتظار العنوان')).toBeVisible();
  });

  test('F03-E1 bad phone is refused', async ({ page }) => {
    await openOrder(page);
    await fill(page, 'شارع ١', 'abc');
    await btn(page, 'أكّد العنوان').click();
    await expect(page.getByText('تأكد من رقم الهاتف وحاول مرة ثانية')).toBeVisible();
    await expect(page.getByLabel('المحافظة')).toBeVisible();
  });

  test('F03-E2 emoji, accents and 200 characters are saved', async ({ page }) => {
    await openOrder(page);
    await fill(page, ('بناية الأمل 🏠 é ' + 'ش'.repeat(200)).slice(0, 200), '0791234567');
    await btn(page, 'أكّد العنوان').click();
    await expect(page.getByText('قيد التجهيز')).toBeVisible();
    const w = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(w).toBeLessThanOrEqual(390);
  });
});
