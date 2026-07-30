---
name: checkout
description: >
  Use this skill when working on paying for a basket. Triggers on: 'checkout', 'payment', 'basket to
  order', 'receipt'.
---

# Checkout and Payment

## Description

As a customer, I want to pay for my basket securely, so that I can complete a purchase with
confidence and know immediately whether it succeeded.

This skill covers the moment a basket becomes an order: converting the current basket into an order
record, handing the total off to a payment provider, and showing (and emailing) a receipt once payment
succeeds. It stops at the receipt — what happens to the order afterwards (status history, cancellation)
belongs to `orders`, and the separate confirmed/shipped transactional emails belong to `notifications`.

Owned by US-09 (Checkout and Payment).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Basket to order conversion** — turn the current basket's line items and total into an order
   record at the moment checkout is started. *(→ AC1)*
2. **Payment provider integration** — submit the order total to a payment provider and act on the
   result: mark the order Paid on success, leave it untouched on decline. *(→ AC2, AC3)*
3. **Receipt shown and emailed on success** — display a receipt immediately after a successful payment
   and send the same receipt by email. *(→ AC4)*
4. **Tests** — coverage for the success and decline paths. *(→ AC2, AC3)*

## Acceptance Criteria

- [ ] AC1: Starting checkout converts the current basket into an order with matching line items and
  total.
- [ ] AC2: A successful payment marks the order Paid.
- [ ] AC3: A declined payment leaves the basket intact and creates no lingering unpaid order.
- [ ] AC4: On successful payment, a receipt is shown on-screen and emailed to the customer.

## Test Cases

| Test Case | Traces to | Input | Expected Result |
|---|---|---|---|
| TC-09-01 | AC2 | Complete checkout with a payment that succeeds | Order is created with status Paid |
| TC-09-02 | AC3 | Complete checkout with a payment that is declined | The basket remains intact; no paid order exists |

## Technical Reference

See `@detailed-designs/checkout.md` for the payment provider abstraction (sandbox/mock in this demo,
a real gateway in production), the exact point in the flow where the basket-to-order conversion
happens, and the receipt template shared between the on-screen view and the emailed copy. Order state
after this point is owned by `orders`, not duplicated here.

## Notes / Gotchas

- This story is **Under Review**; none of the three tasks have started. The decline behavior below —
  basket stays intact, nothing left half-created — is the intended contract, not yet verified.
- Basket-to-order conversion must be idempotent: a retried checkout request (network hiccup, doubled
  click) must not create two orders for one basket. Decide the idempotency key before writing the
  conversion, not after seeing duplicates in testing.
- A declined payment must not leave an abandoned Pending order sitting in the customer's history —
  either no order is created until payment succeeds, or a failed one is cleaned up as part of the
  decline path.
- The receipt here is not the same thing as the order-confirmed email `notifications` sends later:
  the receipt confirms the *transaction*; the confirmation email confirms the *order*. Don't let the
  two skills race to send duplicate emails once both exist.
