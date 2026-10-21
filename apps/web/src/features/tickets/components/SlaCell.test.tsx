import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaCell } from './SlaCell';

afterEach(cleanup);

it.each<[SlaStatus, string]>([
  ['WithinSla', 'Within SLA'], ['AtRisk', 'At risk'], ['Breached', 'Breached'],
  ['Met', 'Met'], ['NotApplicable', 'No SLA'],
])('renders %s with a distinct text label', (slaStatus, label) => {
  render(<SlaCell slaStatus={slaStatus} dueAtUtc={null} />);
  expect(screen.getByText(label)).toBeInTheDocument();
});
