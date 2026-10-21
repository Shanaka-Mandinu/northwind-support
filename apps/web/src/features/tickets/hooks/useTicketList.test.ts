import { act, cleanup, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { page, ticketListItem } from '../../../test/fixtures';
import type { TicketFilters } from '../../../types/api';
import { useTicketList } from './useTicketList';

const fetchMock = vi.fn<typeof fetch>();
const response = () => new Response(JSON.stringify(page([ticketListItem()])));
const queryAt = (index: number) => new URL(String(fetchMock.mock.calls[index][0]), 'http://localhost').searchParams;

async function mount() {
  const hook = renderHook(() => useTicketList());
  await act(async () => { await vi.advanceTimersByTimeAsync(300); });
  return hook;
}

describe('useTicketList requests', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    fetchMock.mockImplementation(async () => response());
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.unstubAllGlobals();
    fetchMock.mockReset();
  });

  it.each<[Partial<TicketFilters>, string, string]>([
    [{ slaStatus: 'AtRisk' }, 'slaStatus', 'AtRisk'],
    [{ status: 'InProgress' }, 'status', 'InProgress'],
    [{ priority: 'High' }, 'priority', 'High'],
    [{ categoryId: 2 }, 'categoryId', '2'],
    [{ customerId: 3 }, 'customerId', '3'],
    [{ assignedAgentId: 4 }, 'assignedAgentId', '4'],
    [{ unassignedOnly: true }, 'unassignedOnly', 'true'],
    [{ page: 2 }, 'page', '2'],
    [{ pageSize: 10 }, 'pageSize', '10'],
    [{ sortBy: 'priority' }, 'sortBy', 'priority'],
    [{ sortDirection: 'asc' }, 'sortDirection', 'asc'],
  ])('immediately requests a changed %s', async (patch, key, value) => {
    const { result } = await mount();
    await act(async () => result.current.updateFilters(patch));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(queryAt(1).get(key)).toBe(value);
  });

  it('debounces typing, including when starting on another page', async () => {
    const { result } = await mount();
    await act(async () => result.current.updateFilters({ page: 2 }));
    const count = fetchMock.mock.calls.length;
    act(() => result.current.updateFilters({ search: 'in' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(200); });
    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(fetchMock).toHaveBeenCalledTimes(count);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(fetchMock).toHaveBeenCalledTimes(count + 1);
    expect(queryAt(count).get('search')).toBe('invoice');
    expect(queryAt(count).get('page')).toBe('1');
  });

  it('applies other filters immediately while search is pending, then preserves them', async () => {
    const { result } = await mount();
    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => result.current.updateFilters({ status: 'InProgress' }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(queryAt(1).get('status')).toBe('InProgress');
    expect(queryAt(1).has('search')).toBe(false);
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(queryAt(2).get('search')).toBe('invoice');
    expect(queryAt(2).get('status')).toBe('InProgress');
    act(() => result.current.updateFilters({ search: '' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(queryAt(3).has('search')).toBe(false);
    await act(async () => result.current.updateFilters({ priority: 'High' }));
    expect(queryAt(4).get('priority')).toBe('High');
  });

  it('resets immediately and cancels pending search', async () => {
    const { result } = await mount();
    await act(async () => result.current.updateFilters({ status: 'Open', page: 2 }));
    act(() => result.current.updateFilters({ search: 'invoice' }));
    await act(async () => result.current.resetFilters());
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(queryAt(2).has('search')).toBe(false);
    expect(queryAt(2).has('status')).toBe(false);
    expect(queryAt(2).get('page')).toBe('1');
    await act(async () => { await vi.advanceTimersByTimeAsync(500); });
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('ignores an older response after a newer request completes', async () => {
    const { result } = await mount();
    let finishOld!: (value: Response) => void;
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => { finishOld = resolve; }));
    await act(async () => result.current.updateFilters({ status: 'Open' }));
    await act(async () => result.current.updateFilters({ status: 'InProgress' }));
    const newest = result.current.data;
    await act(async () => finishOld(new Response(JSON.stringify(page([])))));
    expect(result.current.data).toBe(newest);
  });

  it('reloads current filters and cancels pending search when unmounted', async () => {
    const { result, unmount } = await mount();
    await act(async () => result.current.updateFilters({ status: 'Open' }));
    await act(async () => { await result.current.reload(); });
    expect(queryAt(2).get('status')).toBe('Open');
    act(() => result.current.updateFilters({ search: 'pending' }));
    unmount();
    await act(async () => { await vi.advanceTimersByTimeAsync(500); });
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
});
