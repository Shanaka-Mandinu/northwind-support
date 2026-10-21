import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { ApiError } from '../../../api/client';
import { ticketListItem } from '../../../test/fixtures';
import type { EscalationResult, TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({ ticketsApi: { getEscalations: vi.fn(), escalate: vi.fn() } }));
const getHistory = vi.mocked(ticketsApi.getEscalations);
const escalate = vi.mocked(ticketsApi.escalate);
const ticket: TicketDetail = {
  ...ticketListItem(), description: 'Description',
  customer: { id: 2, name: 'Fabrikam', tier: 'Standard', email: 'test@example.test', phone: null },
  category: { id: 2, name: 'Billing', requiresSpecialist: true },
};

describe('EscalationPanel', () => {
  beforeEach(() => { getHistory.mockResolvedValue([]); });
  afterEach(() => { cleanup(); vi.resetAllMocks(); });

  it('blocks short reasons without calling the API', async () => {
    render(<EscalationPanel ticket={ticket} onUpdated={vi.fn()} />);
    await screen.findByText('No escalations yet.');
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'tiny' } });
    fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: 'Alex' } });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
    expect(escalate).not.toHaveBeenCalled();
  });

  it('shows 409 without changing the ticket or history', async () => {
    escalate.mockRejectedValue(new ApiError(409, 'A Critical ticket cannot be escalated.'));
    const onUpdated = vi.fn();
    render(<EscalationPanel ticket={ticket} onUpdated={onUpdated} />);
    await screen.findByText('No escalations yet.');
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Urgent customer issue' } });
    fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: 'Alex' } });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('A Critical ticket cannot be escalated.');
    expect(onUpdated).not.toHaveBeenCalled();
    expect(getHistory).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Reason')).toHaveValue('Urgent customer issue');
    expect(ticket.priority).toBe('High');
  });

  it('shows pending state then updates the ticket and refreshes history', async () => {
    const onUpdated = vi.fn();
    const result: EscalationResult = {
      ticket: { ...ticket, priority: 'Critical' },
      escalation: {
        id: 1, fromPriority: 'High', toPriority: 'Critical', fromAgent: ticket.assignedAgent,
        toAgent: null, fromDueAtUtc: ticket.dueAtUtc, toDueAtUtc: '2026-10-21T12:00:00Z',
        reason: 'Urgent customer issue', escalatedBy: 'Alex', escalatedAtUtc: '2026-10-21T08:00:00Z',
      },
    };
    let complete!: (value: EscalationResult) => void;
    escalate.mockImplementation(() => new Promise((resolve) => { complete = resolve; }));
    render(<EscalationPanel ticket={ticket} onUpdated={onUpdated} />);
    await screen.findByText('No escalations yet.');
    getHistory.mockResolvedValue([result.escalation]);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Urgent customer issue  ' } });
    fireEvent.change(screen.getByLabelText('Escalated by'), { target: { value: ' Alex ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
    expect(screen.getByRole('button', { name: 'Escalating...' })).toBeDisabled();
    await act(async () => complete(result));
    await waitFor(() => expect(onUpdated).toHaveBeenCalledWith(result.ticket));
    expect(escalate).toHaveBeenCalledWith(ticket.id, { reason: 'Urgent customer issue', escalatedBy: 'Alex' });
    expect(getHistory).toHaveBeenCalledTimes(2);
    expect(screen.getByText('High → Critical')).toBeInTheDocument();
    expect(screen.getByText('Urgent customer issue')).toBeInTheDocument();
  });

  it.each<TicketDetail>([
    { ...ticket, priority: 'Critical' }, { ...ticket, status: 'Resolved' }, { ...ticket, status: 'Closed' },
  ])('does not offer escalation for an ineligible ticket', async (ineligible) => {
    render(<EscalationPanel ticket={ineligible} onUpdated={vi.fn()} />);
    await screen.findByText('No escalations yet.');
    expect(screen.queryByRole('button', { name: 'Escalate ticket' })).not.toBeInTheDocument();
  });
});
