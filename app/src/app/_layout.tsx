import { useEffect } from 'react';
import { I18nManager, Platform } from 'react-native';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import {
  useFonts, IBMPlexSansArabic_400Regular, IBMPlexSansArabic_500Medium, IBMPlexSansArabic_600SemiBold, IBMPlexSansArabic_700Bold,
} from '@expo-google-fonts/ibm-plex-sans-arabic';
import { fonts, useColors } from '../theme';
import { t } from '../i18n';

// Arabic first: the whole app runs right-to-left.
if (Platform.OS !== 'web' && !I18nManager.isRTL) {
  I18nManager.allowRTL(true);
  I18nManager.forceRTL(true);
}

export default function RootLayout() {
  const c = useColors();
  const [loaded] = useFonts({ IBMPlexSansArabic_400Regular, IBMPlexSansArabic_500Medium, IBMPlexSansArabic_600SemiBold, IBMPlexSansArabic_700Bold });
  useEffect(() => {
    if (Platform.OS === 'web') { document.documentElement.dir = 'rtl'; document.documentElement.lang = 'ar'; }
  }, []);
  if (!loaded) return null;
  return (
    <SafeAreaProvider>
      <StatusBar style="auto" />
      <Stack screenOptions={{
        headerStyle: { backgroundColor: c.bg }, headerTintColor: c.text, headerShadowVisible: false,
        headerTitleStyle: { fontFamily: fonts[600], fontSize: 18 }, contentStyle: { backgroundColor: c.bg },
        headerBackButtonDisplayMode: 'minimal',
      }}>
        <Stack.Screen name="index" options={{ headerShown: false }} />
        <Stack.Screen name="onboarding" options={{ headerShown: false }} />
        <Stack.Screen name="login" options={{ headerShown: false }} />
        <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
        <Stack.Screen name="product/[id]" options={{ title: '' }} />
        <Stack.Screen name="topup" options={{ title: t.topup.title, presentation: 'modal' }} />
        <Stack.Screen name="result/[id]" options={{ title: '' }} />
        <Stack.Screen name="order/[id]" options={{ title: t.order.title }} />
        <Stack.Screen name="owners" options={{ title: t.owners.title }} />
        <Stack.Screen name="redeem/[id]" options={{ title: t.redeem.title }} />
        <Stack.Screen name="notifications" options={{ title: t.notifications.title }} />
        <Stack.Screen name="help" options={{ title: t.help.title }} />
      </Stack>
    </SafeAreaProvider>
  );
}
