import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'com.zeno.store',
  appName: 'ZENO',
  webDir: 'dist',
  backgroundColor: '#0b0b0f',
  android: { allowMixedContent: false },
  ios: { contentInset: 'always' }
};

export default config;
