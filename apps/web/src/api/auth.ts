import type { CurrentUser, LoginRequest, LoginResponse } from '../types/api';
import { request } from './client';

export { getStoredSession, saveSession, clearSession } from '../features/auth/session';

export const authApi = {
  login: (payload: LoginRequest) => request<LoginResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(payload),
  }),
  me: () => request<CurrentUser>('/auth/me'),
};
