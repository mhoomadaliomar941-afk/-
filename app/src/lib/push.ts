import { Platform } from 'react-native';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import Constants from 'expo-constants';
import { api, backend } from '../data';

/** Asks once for permission and registers this device's Expo push token. Quiet on failure:
 *  the in-app inbox still shows everything. Needs an EAS project id (app.json extra.eas.projectId). */
export async function registerForPush() {
  if (backend !== 'supabase' || Platform.OS === 'web' || !Device.isDevice) return;
  try {
    const projectId = Constants.expoConfig?.extra?.eas?.projectId as string | undefined;
    if (!projectId) return;
    let { status } = await Notifications.getPermissionsAsync();
    if (status !== 'granted') status = (await Notifications.requestPermissionsAsync()).status;
    if (status !== 'granted') return;
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', { name: 'سبوت', importance: AndroidImportance.DEFAULT });
    }
    const { data: token } = await Notifications.getExpoPushTokenAsync({ projectId });
    await api.registerPushToken(token, Platform.OS === 'ios' ? 'ios' : 'android');
  } catch {
    // no push on this device; nothing else depends on it
  }
}
