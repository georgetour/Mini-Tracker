---
name: user-management
description: >
  Use this skill when working on customer accounts. Triggers on: 'sign-up', 'sign-in', 'profile
  editing', 'password reset'.
---

# User Management

## Description

As a customer, I want to create an account and manage my profile, so that my orders and preferences
are remembered between visits instead of starting over every time.

This skill covers the account lifecycle: registering, signing in and out, editing the profile a
customer sees of themselves, and recovering access when a password is forgotten. It does not cover
what a signed-in customer *sees* elsewhere in the app (that's `user-menus`), only the account itself.

Owned by US-04 (User Management).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Sign-up, sign-in, sign-out** — register with email and password, authenticate on return visits,
   and end the session cleanly on sign-out. *(→ AC1, AC2, AC3)*
2. **Profile editing** — a signed-in customer can view and update their display name. *(→ AC4)*
3. **Password reset by email** — request a reset link by email and set a new password from it.
   *(→ AC5)*
4. **Tests** — coverage for registration, duplicate-email handling, and sign-out. *(→ AC1, AC2, AC3)*

## Acceptance Criteria

- [ ] AC1: Registering with a new email creates the account and signs the customer in immediately.
- [ ] AC2: Registering with an email already in use shows a clear error and creates no duplicate
  account.
- [ ] AC3: Signing out ends the session; protected pages then redirect to sign-in.
- [ ] AC4: A signed-in customer can view and edit their display name.
- [ ] AC5: A customer who has forgotten their password can reset it via an emailed link.

## Test Cases

| Test Case | Traces to | Input | Expected Result |
|---|---|---|---|
| TC-04-1 | AC1 | Register with a new email | Account created, customer signed in |
| TC-04-2 | AC2 | Register with an email already in use | Clear error shown, no duplicate account created |
| TC-04-3 | AC3 | Sign out | Session ends; protected pages redirect to sign-in |

## Technical Reference

See `@detailed-designs/user-management.md` for the account data model, password hashing and reset
token handling, and session/cookie configuration.

## Notes / Gotchas

- Duplicate-email checks must be case-insensitive on the email's domain and local part, otherwise
  `A@Example.com` and `a@example.com` silently create two accounts.
- Reset tokens must be single-use and time-limited; a token that still works after the password has
  already been changed is a real vulnerability, not an edge case.
- Sign-out should invalidate the session server-side, not just clear the client-side cookie, so a
  captured cookie can't be replayed after the customer signs out.
