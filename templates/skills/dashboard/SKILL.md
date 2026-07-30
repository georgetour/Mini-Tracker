---
name: dashboard
description: >
  Use this skill when working on the signed-in customer summary. Triggers on: 'customer dashboard',
  'recent orders panel', 'spend this month'.
---

# Customer Dashboard

## Description

As a signed-in customer, I want a dashboard summarising recent activity, so that I can pick up where
I left off instead of re-navigating to orders or re-deriving how much I've spent this month.

This skill covers the dashboard's two panels: a list of recent orders, and a running total of what
the customer has spent so far in the current calendar month. It is presentation over data other
skills already own — order data comes from `orders`; this skill adds no order records of its own,
only summarizes them.

Owned by US-10 (Customer Dashboard).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Recent orders panel** — show the customer's five most recent orders, most recent first, each
   linking through to its detail page. *(→ AC1)*
2. **Spend-this-month summary** — total what the customer has spent in the current calendar month, so
   far. *(→ AC2)*
3. **Tests** — coverage for the recent-orders panel. *(→ AC1)*

## Acceptance Criteria

- [ ] AC1: The dashboard shows the signed-in customer's five most recent orders, most recent first.
- [ ] AC2: The dashboard shows a running total of the customer's spend for the current calendar month.

## Test Cases

| Test Case | Traces to | Input | Expected Result |
|---|---|---|---|
| TC-10-01 | AC1 | Load the dashboard as a signed-in customer with order history | The five most recent orders are shown, most recent first |

## Technical Reference

See `@detailed-designs/dashboard.md` for how the recent-orders query is scoped and limited, and which
order statuses count toward the month-spend total. Both panels read from `orders`' data model; the
dashboard itself stores nothing new.

## Notes / Gotchas

- This story is **Refined** — reviewed and ready to build, but neither task has actually started yet.
  Treat "five most recent orders, most recent first" as the agreed design, not a shipped behavior.
- The month-spend total should count Paid (and later) orders, not Pending ones — a basket mid-checkout
  shouldn't inflate the figure before payment has actually succeeded. Confirm this against `orders`'
  status set once that story lands, since dashboard depends on it rather than defining it independently.
- A customer with no order history yet should see a friendly empty state on both panels, not a blank
  panel or a zero that reads as a possible bug.
- "This month" should use a calendar-month boundary, not a rolling 30 days — a customer glancing at
  the dashboard on the 1st should see the total reset, not a smeared trailing window.
- The recent-orders panel and the month-spend total can disagree in scope on purpose: an order placed
  last month still belongs in "five most recent," even though it's excluded from this month's spend.
  Don't let one query's date filter leak into the other's.
