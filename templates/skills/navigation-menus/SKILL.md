---
name: navigation-menus
description: >
  Use this skill when working on site navigation. Triggers on: 'signed-out menu', 'signed-in menu',
  'navigation state'.
---

# Navigation Menus

## Description

As a customer, I want the navigation to reflect whether I am signed in, so that I always see the
actions available to me instead of hunting for a Sign in link buried behind an account menu, or
seeing account options I can't actually use yet.

This skill covers the two menu states the top navigation can be in: signed-out (visitor) and
signed-in (customer). It depends on `user-management` for the authentication state itself but owns
the navigation's content and layout in each state.

Owned by US-05 (Navigation Menus).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Signed-out menu** — Browse, Sign in, Register: the minimal set a visitor needs to explore the
   catalog or start an account. *(→ AC1)*
2. **Signed-in menu** — Orders, Wishlist, Profile, Sign out: the account-scoped actions a customer
   needs once authenticated. *(→ AC2)*
3. **Tests** — verify menu contents for both signed-out and signed-in states. *(→ AC1, AC2)*

## Acceptance Criteria

- [ ] AC1: A signed-out visitor sees only Browse, Sign in, and Register — no account-scoped entries.
- [ ] AC2: A signed-in customer sees Orders, Wishlist, Profile, and Sign out.

## Technical Reference

See `@detailed-designs/user-menus.md` for the menu component's structure and how it reads
authentication state without an extra round-trip on every page load. The two menus share one
component and one set of styles; only the list of entries differs by state, so a new entry added to
one menu should never require duplicating layout code into the other.

## Notes / Gotchas

- The menu must react to sign-in/sign-out without a full page reload wherever the app already avoids
  one, or a customer who just signed in will still see the signed-out menu until they navigate again.
- Don't let a slow authentication check flash the signed-out menu before swapping to signed-in — that
  reads as a bug even though the final state is correct. Prefer a brief loading state over a wrong one.
- Wishlist appears in the signed-in menu even before US-13 ships fully; the link should degrade
  gracefully rather than 404 while wishlist is still on hold.
- Mobile and desktop share the same entry list; only the presentation (a dropdown vs. an inline bar)
  differs, so tests should assert on menu contents rather than on a specific layout.
