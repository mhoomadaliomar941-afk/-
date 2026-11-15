import { test as base, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

/** Every test: fail on console errors, page errors and 5xx. `a11y()` scans the current screen. */
export const test = base.extend<{ a11y: (screen: string) => Promise<void> }>({
  page: async ({ page }, use) => {
    const problems: string[] = [];
    page.on('console', (m) => { if (m.type() === 'error') problems.push(`console: ${m.text()}`); });
    page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`));
    page.on('response', (r) => { if (r.status() >= 500) problems.push(`${r.status()} ${r.url()}`); });
    await use(page);
    expect(problems, 'console errors / 5xx during the test').toEqual([]);
  },
  a11y: async ({ page }, use) => {
    await use(async (screen) => {
      await page.waitForTimeout(400); // let skeletons resolve
      const r = await new AxeBuilder({ page: page as unknown as ConstructorParameters<typeof AxeBuilder>[0]['page'] }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
      const found = r.violations.map((v) => `${screen}: ${v.id} (${v.impact}) ×${v.nodes.length}: ${v.nodes[0]?.target.join(' ')}`);
      expect.soft(found, `axe on ${screen}`).toEqual([]);
    });
  },
});
export { expect };

export const btn = (page: Page, name: string | RegExp) => page.getByRole('button', { name, exact: typeof name === 'string' });

/** Tab bar entries are links or tabs depending on the navigator; match either. */
export const tab = (page: Page, name: string) =>
  page.getByRole('tab', { name, exact: true }).or(page.getByRole('link', { name, exact: true })).first();

/** Logs in as a member; pass '790000000' to log in as the owner/admin (demo data layer). */
export async function login(page: Page, phone = '791234567') {
  await page.goto('/');
  await btn(page, 'تخطّي').click();
  await page.getByTestId('phone').fill(phone);
  await btn(page, 'أرسل الرمز').click();
  await page.getByTestId('otp').fill('123456');
  await expect(page.getByText('منتجات مفتوحة')).toBeVisible();
}

/** Hidden tab screens stay in the DOM: pick the visible match. */
export const visible = (page: Page, role: 'button', name: RegExp) => page.getByRole(role, { name }).locator('visible=true').last();
