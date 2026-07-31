---
name: product-catalog
description: >
  Use this skill when working on product browsing. Triggers on: 'product catalog', 'category
  listing', 'search and filter'.
---

# Product Catalog

## Description

As a customer, I want to browse and search products by category, so that I can find what I want
without scrolling through everything the store carries.

This skill covers the catalog surface end to end: the category model products belong to, paginated
product listings scoped to a category, and search/filter on top of that listing. It's the largest
single feature area in Epic 1 and the one most other stories (checkout, wishlist, dashboard) point
back into.

Owned by US-06 (Product Catalog).

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Category model and seed data** — categories exist as first-class records, and demo products are
   assigned to them so the catalog has something to browse out of the box. *(→ AC1)*
2. **Product listing with pagination** — list a category's products a page at a time rather than all
   at once. *(→ AC2)*
3. **Search and filter** — match partial product names, and narrow a listing by category. *(→ AC3, AC4)*
4. **Empty-state handling** — a category with no products shows a friendly message instead of an
   error. *(→ AC5)*
5. **Tests** — listing, search, and empty-state coverage. *(→ all AC)*

## Acceptance Criteria

- [ ] AC1: Categories are seeded and every product belongs to at least one.
- [ ] AC2: A category's product listing paginates rather than returning everything in one response.
- [ ] AC3: Search matches partial product names, not just exact ones.
- [ ] AC4: Filtering by category returns only that category's products.
- [ ] AC5: A category with zero products shows a friendly empty state, not a raw error.

## Technical Reference

See `@detailed-designs/catalog.md` for the category/product data model, the pagination contract, and
how search and category filters compose in the same query.

## Notes / Gotchas

- **TC-06-03 is currently failing**: an empty category shows a raw error page instead of the intended
  empty state. This is the one open defect in the story and blocks it from moving to Done — fix the
  empty-listing path before touching anything else here.
- Pagination and search/filter must compose: a search within a filtered category should paginate the
  filtered-and-searched result set, not the unfiltered one.
- Category and product seed data doubles as the demo content new installs see — keep it realistic
  enough that a first-run screenshot doesn't look obviously fake.
