---
name: notifications
description: >
  Use this skill when working on transactional email. Triggers on: 'order confirmation email',
  'shipped email', 'email templates'.
---

# Email Notifications

## Description

As a customer, I want an email when my order is confirmed or shipped, so that I do not have to keep
checking the site for updates.

This skill covers transactional email tied to an order's lifecycle: the templates themselves, and the
two triggers that send them — an order moving to Confirmed, and an order moving to Shipped. It is
distinct from the receipt `checkout` shows and emails at the moment of payment: the receipt confirms
the transaction that just happened; this skill's emails confirm order-lifecycle events that can happen
later, including ones with no customer present to see a page at all (a warehouse marking an order
shipped, for instance).

Owned by US-11 (Email Notifications).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Transactional email templates** — one template for order-confirmed, one for order-shipped, both
   carrying enough order detail (items, total, status) to stand alone without the customer visiting
   the site. *(→ AC1)*
2. **Send on order confirmed and shipped** — queue the matching email the moment an order's status
   transitions to Confirmed or to Shipped. *(→ AC2, AC3)*
3. **Tests** — coverage for the confirmed-email trigger. *(→ AC2)*

## Acceptance Criteria

- [ ] AC1: A templated email exists for both the order-confirmed and order-shipped events.
- [ ] AC2: An order transitioning to Confirmed queues a confirmation email to the customer.
- [ ] AC3: An order transitioning to Shipped queues a shipped email to the customer.

## Test Cases

| Test Case | Traces to | Input | Expected Result |
|---|---|---|---|
| TC-11-01 | AC2 | An order transitions to Confirmed | A confirmation email is queued for that order |

## Technical Reference

See `@detailed-designs/notifications.md` for the template content, the sender/from-address
configuration, and how the send hooks into `orders`' status transitions rather than polling for
changes. Each transition should fire its own queued send, not a shared batch job that scans for
"anything that changed recently."

## Notes / Gotchas

- This story hasn't started — **Not Yet Started**, both tasks open, targeted at V1.5 after Orders and
  Checkout (V1) ship. It depends on `orders`' status model existing before it has anything to hook into.
- Sending must be queued, not synchronous inside the request that changes the order's status — a slow
  or down email provider should never delay confirming an order or marking it shipped for the customer
  waiting on that response.
- Don't double-send: a status transition fired twice by mistake (a retried write, a race) should not
  queue two identical emails. The send itself should be idempotent per order-and-event, not just the
  status write.
- Keep this genuinely separate from `checkout`'s receipt. They can look similar in an inbox, but they
  answer different questions — "did my payment work?" versus "where is my order now?" — and a
  customer who never gets the second one because it looked redundant to skip is a real regression.
