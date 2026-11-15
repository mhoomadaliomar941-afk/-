import { fakeApi } from './fake';
import { createSupabaseApi } from './supabase';
import type { Api } from './types';
export * from './types';

// Real backend when both public env vars are set (app/.env.local), otherwise the fake layer.
// No screen imports fake.ts or supabase.ts directly.
const url = process.env.EXPO_PUBLIC_SUPABASE_URL;
const key = process.env.EXPO_PUBLIC_SUPABASE_ANON_KEY;
export const backend: 'supabase' | 'fake' = url && key ? 'supabase' : 'fake';
export const api: Api = url && key ? createSupabaseApi(url, key) : fakeApi;
