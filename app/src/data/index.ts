import { fakeApi } from './fake';
import type { Api } from './types';
export * from './types';

// Swap for the Supabase implementation in /replica-backend. No screen imports fake.ts directly.
export const api: Api = fakeApi;
