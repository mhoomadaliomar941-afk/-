import { test, expect, btn, login, tab } from './fixtures';

test.describe('F00 log in', () => {
  test('F00-H1 phone + code reaches the feed', async ({ page, a11y }) => {
    await page.goto('/');
    await expect(page.getByText('اختار المنتج')).toBeVisible();
    await a11y('S01 onboarding');
    await btn(page, 'تخطّي').click();
    await a11y('S02 login');
    await page.getByTestId('phone').fill('791234567');
    await btn(page, 'أرسل الرمز').click();
    await expect(page.getByText('أدخل الرمز')).toBeVisible();
    await a11y('S02 code');
    await page.getByTestId('otp').fill('123456');
    await expect(page.getByText('منتجات مفتوحة')).toBeVisible();
    await a11y('S03 feed');
  });

  test('F00-E1 empty phone keeps send disabled', async ({ page }) => {
    await page.goto('/login');
    await expect(btn(page, 'أرسل الرمز')).toHaveAttribute('aria-disabled', 'true');
  });

  test('F00-E2 non-Jordanian mobile shows an error', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('phone').fill('612345678');
    await btn(page, 'أرسل الرمز').click();
    await expect(page.getByText('أدخل رقم أردني صحيح يبدأ بـ 7')).toBeVisible();
    await expect(page.getByTestId('phone')).toBeVisible();
  });

  test('F00-E3 letters and emoji are dropped from the phone field', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('phone').fill('7a9😀1 234 567');
    await expect(page.getByTestId('phone')).toHaveValue('79 123 4567');
  });

  test('F00-E3b BUG-004 pasted +962 number keeps every digit and signs in', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('phone').fill('+962 79 123 4567');
    await expect(page.getByTestId('phone')).toHaveValue('79 123 4567');
    await btn(page, 'أرسل الرمز').click();
    await expect(page.getByText('أدخل الرمز')).toBeVisible();
  });

  test('F00-N1 wrong code: error, boxes cleared, retry works', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('phone').fill('791234567');
    await btn(page, 'أرسل الرمز').click();
    await page.getByTestId('otp').fill('000000');
    await expect(page.getByText('الرمز غلط، جرّب مرة ثانية')).toBeVisible();
    await expect(page.getByTestId('otp')).toHaveValue('');
    await page.getByTestId('otp').fill('123456');
    await expect(page.getByText('منتجات مفتوحة')).toBeVisible();
  });

  test('F00-E4 deep link to wallet while signed out shows no wallet', async ({ page }) => {
    await page.goto('/wallet');
    await expect(page.getByText(/اختار المنتج|أهلاً فيك/)).toBeVisible();
    await expect(page.getByText('الحركات')).toHaveCount(0);
  });

  test('F00-E5 sign out, then back button stays signed out', async ({ page }) => {
    await login(page);
    await tab(page, 'حسابي').click();
    await btn(page, 'تسجيل الخروج').click();
    await expect(page.getByText('أهلاً فيك')).toBeVisible();
    await page.goBack();
    await expect(page.getByText('أهلاً فيك')).toBeVisible();
    await expect(page.getByText('منتجات مفتوحة')).toHaveCount(0);
  });

  test('F00-E6 keyboard only', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('phone').focus();
    await page.keyboard.type('791234567');
    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');
    await expect(page.getByText('أدخل الرمز')).toBeVisible();
    await page.keyboard.type('123456');
    await expect(page.getByText('منتجات مفتوحة')).toBeVisible();
  });
});
