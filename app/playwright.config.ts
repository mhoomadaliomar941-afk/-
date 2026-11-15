import { defineConfig, devices } from '@playwright/test';

// Runs against the static web build on the fake data layer (seed data, no network).
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: 0,
  reporter: [['list'], ['json', { outputFile: 'e2e/results.json' }]],
  use: {
    baseURL: 'http://localhost:8123',
    viewport: { width: 390, height: 844 },
    locale: 'ar-JO',
    timezoneId: 'Asia/Amman',
    trace: 'off',
  },
  projects: [{ name: 'mobile-chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } }],
  webServer: {
    command: 'EXPO_OFFLINE=1 CI=1 npx expo export -p web >/dev/null && node e2e/serve.cjs dist 8123',
    url: 'http://localhost:8123',
    reuseExistingServer: true,
    timeout: 300_000,
    env: { EXPO_PUBLIC_SUPABASE_URL: '', EXPO_PUBLIC_SUPABASE_ANON_KEY: '' },
  },
});
