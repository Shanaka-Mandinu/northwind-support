import type { AuthSession } from '../../types/api';

const STORAGE_KEY = 'northwind-support-auth';
const SESSION_EVENT = 'northwind-session-changed';

export function readSessionRaw(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

export function parseSession(raw: string | null): AuthSession | null {
  try {
    const value: unknown = raw ? JSON.parse(raw) : null;
    if (!value || typeof value !== 'object' || !('token' in value) || typeof value.token !== 'string' || !value.token
      || !('expiresAtUtc' in value) || typeof value.expiresAtUtc !== 'string'
      || !Number.isFinite(Date.parse(value.expiresAtUtc)) || Date.parse(value.expiresAtUtc) <= Date.now()
      || !('agent' in value) || !value.agent || typeof value.agent !== 'object'
      || !('id' in value.agent) || typeof value.agent.id !== 'number' || !Number.isInteger(value.agent.id) || value.agent.id <= 0
      || !('fullName' in value.agent) || typeof value.agent.fullName !== 'string'
      || !('email' in value.agent) || typeof value.agent.email !== 'string') return null;
    return { token: value.token, expiresAtUtc: value.expiresAtUtc,
      agent: { id: value.agent.id, fullName: value.agent.fullName, email: value.agent.email } };
  } catch { return null; }
}

export const getStoredSession = () => parseSession(readSessionRaw());

export function saveSession(session: AuthSession) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  window.dispatchEvent(new Event(SESSION_EVENT));
}

export function clearSession() {
  try { localStorage.removeItem(STORAGE_KEY); } finally {
    window.dispatchEvent(new Event(SESSION_EVENT));
  }
}

export function subscribeSession(listener: () => void) {
  window.addEventListener(SESSION_EVENT, listener);
  window.addEventListener('storage', listener);
  return () => {
    window.removeEventListener(SESSION_EVENT, listener);
    window.removeEventListener('storage', listener);
  };
}
