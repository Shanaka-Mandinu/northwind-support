import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { authApi, getStoredSession } from '../../api/auth';
import { ApiError } from '../../api/client';
import { LoginPage } from './LoginPage';

vi.mock('../../api/auth', async () => {
  const actual = await vi.importActual<typeof import('../../api/auth')>('../../api/auth');
  return {
    ...actual,
    authApi: { login: vi.fn(), me: vi.fn() },
  };
});

describe('LoginPage', () => {
  const login = vi.mocked(authApi.login);

  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    cleanup();
    vi.resetAllMocks();
  });

  it('stores the session and redirects after a successful login', async () => {
    login.mockResolvedValue({
      token: 'jwt-token',
      expiresAtUtc: '2030-01-01T00:00:00Z',
      agent: { id: 7, fullName: 'Alex Turner', email: 'alex@example.com' },
    });

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/tickets" element={<div>Tickets page</div>} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'alex@example.com' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'correct-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(login).toHaveBeenCalledWith({ email: 'alex@example.com', password: 'correct-password' }));
    await waitFor(() => expect(getStoredSession()).not.toBeNull());
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument());
  });

  it('shows the failure message and clears the stored session when login fails', async () => {
    login.mockRejectedValue(new ApiError(401, 'Invalid email or password.'));

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/tickets" element={<div>Tickets page</div>} />
        </Routes>
      </MemoryRouter>,
    );

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'alex@example.com' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong-password' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.');
    expect(getStoredSession()).toBeNull();
  });
});
