import { test, expect, btn, login, tab } from './fixtures';
import type { Page } from '@playwright/test';

const ADMIN = '790000000';
const cashLine = (page: Page) => page.getByText('الرصيد', { exact: true }).locator('xpath=..');

async function openTopup(page: Page, phone?: string) {
  await login(page, phone);
  await tab(page, 'المحفظة').click();
  await btn(page, 'اشحن').click();
  await expect(page.getByText('طريقة الدفع')).toBeVisible();
}

test.describe('F05 top up', () => {
  test('F05-H1 5 JD by card lands in the balance and the ledger', async ({ page, a11y }) => {
    await openTopup(page);
    await a11y('S06 top-up');
    await page.getByRole('radio', { name: /بطاقة بنكية/ }).click();
    await page.getByRole('button', { name: /^ادفع/ }).click();
    await expect(page.getByText('الحركات')).toBeVisible();
    await expect(cashLine(page)).toContainText('9.000');
    await expect(page.getByText(/\+5\.000/).first()).toBeVisible();
    await a11y('S07 wallet');
  });

  test('F05-E1 other amount', async ({ page }) => {
    await openTopup(page);
    await page.getByLabel('مبلغ آخر').fill('7');
    await expect(page.getByRole('button', { name: /^ادفع/ })).toContainText('7.000');
  });

  test('F05-E2 methods: CliQ first; Google Pay on Android/web, no Apple Pay', async ({ page }) => {
    await openTopup(page);
    await expect(page.getByRole('radio', { name: /CliQ/ })).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByRole('radio', { name: 'Google Pay' })).toBeVisible();
    await expect(page.getByRole('radio', { name: 'Apple Pay' })).toHaveCount(0);
  });

  test('F05-H2 CliQ: request, "sent", the owner approves, the balance arrives once', async ({ page, a11y }) => {
    page.on('dialog', (d) => d.accept());
    await openTopup(page, ADMIN); // the demo admin is also a member: one session covers both sides
    await page.getByRole('radio', { name: /10\.000/ }).click();
    await page.getByRole('button', { name: /^ادفع/ }).click();
    await expect(page.getByText('بانتظار تحويلك')).toBeVisible();
    await expect(page.getByText('NAZIH-DEMO')).toBeVisible();
    const ref = (await page.getByText(/^NZ-\d{6}$/).innerText()).trim();
    await a11y('CliQ transfer');
    await btn(page, 'حوّلت').click();
    await expect(page.getByText('بانتظار تأكيدنا')).toBeVisible();
    await btn(page, 'رجوع للمحفظة').click();
    await expect(cashLine(page)).toContainText('4.000'); // not credited yet
    await tab(page, 'حسابي').click();
    await btn(page, 'لوحة الإدارة').click();
    await btn(page, /^طلبات شحن CliQ/).click();
    await expect(page.getByText(ref)).toBeVisible();
    await a11y('admin CliQ');
    await btn(page, 'تم الاستلام').click();
    await expect(page.getByText('انشحنت محفظتك').last()).toBeVisible();
    await expect(btn(page, 'تم الاستلام')).toHaveCount(0); // decided: no second approval possible
    await page.goBack(); await page.goBack();
    await tab(page, 'المحفظة').click();
    await expect(cashLine(page)).toContainText('14.000');
  });

  test('F05-N3 CliQ: the owner rejects with a reason; nothing credited', async ({ page }) => {
    await openTopup(page, ADMIN);
    await page.getByRole('button', { name: /^ادفع/ }).click();
    await btn(page, 'حوّلت').click();
    await btn(page, 'رجوع للمحفظة').click();
    await tab(page, 'حسابي').click();
    await btn(page, 'لوحة الإدارة').click();
    await btn(page, /^طلبات شحن CliQ/).click();
    await btn(page, 'رفض').click();
    await page.getByLabel('السبب (بيوصل للعضو)').fill('ما وصل تحويل بهالرقم');
    await btn(page, 'أكّد الرفض').click();
    await expect(page.getByText('ما وصل تحويل بهالرقم').last()).toBeVisible();
    await page.goBack(); await page.goBack();
    await tab(page, 'المحفظة').click();
    await expect(cashLine(page)).toContainText('4.000');
  });

  test('F05-E3 the owner turns CliQ off: it disappears from top-up', async ({ page }) => {
    await login(page, ADMIN);
    await tab(page, 'حسابي').click();
    await btn(page, 'لوحة الإدارة').click();
    await btn(page, 'الإعدادات').click();
    await page.getByLabel('الاسم المستعار (Alias) على CliQ').fill('');
    await btn(page, 'حفظ').click();
    await expect(page.getByText('انحفظ')).toBeVisible();
    await page.goBack(); await page.goBack();
    await tab(page, 'المحفظة').click();
    await btn(page, 'اشحن').click();
    await expect(page.getByText('طريقة الدفع')).toBeVisible();
    await expect(page.getByRole('radio', { name: /CliQ/ })).toHaveCount(0);
    await expect(page.getByRole('radio', { name: /بطاقة بنكية/ })).toHaveAttribute('aria-checked', 'true');
  });
});
