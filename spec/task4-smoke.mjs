// Run against a freshly seeded Compose stack. This check does not change data.
// TASK4_API_URL=http://localhost:5080 node spec/task4-smoke.mjs
import assert from 'node:assert/strict';

const base = process.env.TASK4_API_URL ?? 'http://localhost:5080';
let healthy = false;
for (let attempt = 0; attempt < 60; attempt++) {
  try {
    const response = await fetch(`${base}/health`, { signal: AbortSignal.timeout(5000) });
    healthy = response.status === 200 && await response.text() === 'Healthy';
    if (healthy) break;
  } catch { /* Startup includes migrations and seed data. */ }
  await new Promise(resolve => setTimeout(resolve, 1000));
}
assert.ok(healthy, 'Anonymous /health must become healthy after startup');
assert.equal((await fetch(`${base}/api/tickets`)).status, 401);
const login = await fetch(`${base}/api/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'alex.turner@northwind-support.example', password: 'NorthwindDev!2026' }),
});
assert.equal(login.status, 200);
const session = await login.json();
const headers = { Authorization: `Bearer ${session.token}` };
const tickets = await fetch(`${base}/api/tickets?pageSize=100`, { headers });
assert.equal(tickets.status, 200);
assert.equal((await tickets.json()).totalCount, 40);
for (const [resource, count] of [['agents', 6], ['customers', 6], ['categories', 5]]) {
  const response = await fetch(`${base}/api/${resource}`, { headers });
  assert.equal(response.status, 200);
  assert.equal((await response.json()).length, count);
}
console.log('PASS: public health, protected API, fixture login, 40 tickets, 6 agents, 6 customers, 5 categories.');
