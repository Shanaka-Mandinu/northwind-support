import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { request } from '../../api/client';
import { RequireAuth } from './RequireAuth';
import { clearSession, getStoredSession, parseSession, saveSession } from './session';

const session = () => ({ token: 'test-token', expiresAtUtc: new Date(Date.now() + 3600000).toISOString(),
  agent: { id: 1, fullName: 'Alex Turner', email: 'alex@example.com' } });

function LoginDestination() {
  const location = useLocation();
  return <div>Login: {location.state?.from}</div>;
}

function renderProtected() {
  render(<MemoryRouter initialEntries={['/tickets/7?tab=history#latest']}><Routes>
    <Route path="/login" element={<LoginDestination />} />
    <Route path="/tickets/:id" element={<RequireAuth><div>Protected ticket</div></RequireAuth>} />
  </Routes></MemoryRouter>);
}

afterEach(() => { cleanup(); localStorage.clear(); vi.unstubAllGlobals(); vi.useRealTimers(); });

describe('authenticated session flow', () => {
  it('preserves the full destination when authentication is required', () => {
    renderProtected();
    expect(screen.getByText('Login: /tickets/7?tab=history#latest')).toBeInTheDocument();
  });

  it('rejects malformed and expired stored sessions', () => {
    for (const raw of ['broken', '{}', JSON.stringify({ ...session(), expiresAtUtc: 'invalid' }),
      JSON.stringify({ ...session(), expiresAtUtc: new Date(Date.now() - 1).toISOString() })]) {
      expect(parseSession(raw)).toBeNull();
    }
  });

  it('attaches the token and redirects after an API 401', async () => {
    saveSession(session());
    renderProtected();
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    await act(async () => { await expect(request('/tickets')).rejects.toMatchObject({ status: 401 }); });
    expect(new Headers(fetchMock.mock.calls[0][1].headers).get('Authorization')).toBe('Bearer test-token');
    expect(getStoredSession()).toBeNull();
    expect(screen.getByText('Login: /tickets/7?tab=history#latest')).toBeInTheDocument();
  });

  it('does not clear a newer login when an older request fails', async () => {
    saveSession(session());
    let resolve!: (response: Response) => void;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(done => { resolve = done; })));
    const pending = request('/tickets');
    saveSession({ ...session(), token: 'new-token' });
    resolve(new Response('', { status: 401 }));
    await expect(pending).rejects.toMatchObject({ status: 401 });
    expect(getStoredSession()?.token).toBe('new-token');
  });

  it('redirects when the session expires while the page is open', () => {
    vi.useFakeTimers();
    saveSession({ ...session(), expiresAtUtc: new Date(Date.now() + 1000).toISOString() });
    renderProtected();
    expect(screen.getByText('Protected ticket')).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(1001));
    expect(getStoredSession()).toBeNull();
    expect(screen.getByText('Login: /tickets/7?tab=history#latest')).toBeInTheDocument();
  });

  it('redirects immediately when logging out', () => {
    saveSession(session());
    renderProtected();
    act(() => clearSession());
    expect(screen.getByText('Login: /tickets/7?tab=history#latest')).toBeInTheDocument();
  });
});
