# 0003 – Frontend stack and UI kit

**Status:** Accepted (M1)
**Date:** 2026-11-03

## Context

The brief proposes React + TS + Vite, React Router, TanStack Query, shadcn/ui + Tailwind, Recharts, react-hook-form + zod. The calendar must be accessible (ARIA grid, keyboard) and responsive; strings must be i18n-ready. React Router v8 shipped 2026-06-17 (ESM-only; requires React ≥ 19.2.7, Node ≥ 22.22; `react-router-dom` removed). shadcn/ui's Vite guide uses Tailwind v4 via `@tailwindcss/vite` and `@import "tailwindcss"`.

## Decision

- **Build**: Vite (latest stable, ≥ 7) + `@vitejs/plugin-react`, TypeScript `strict` + `noUncheckedIndexedAccess`, path alias `@/* → src/*`. npm is the package manager (lockfile committed); Node pinned in `web/.nvmrc` (`26`).
- **Routing**: `react-router` v8 in **data mode** (`createBrowserRouter` from `react-router`, `RouterProvider` from `react-router/dom`), client-side SPA only. Data mode (not framework mode) gives route objects, `errorElement`s and lazy routes without SSR/server build; data fetching stays in TanStack Query (route loaders not used for API calls in M1). Route skeleton: `/` → redirect `/diary/:year/:month` (current month), `/diary/:year/:month`, `/insights`, `/signin` (placeholder until M3), `*` → NotFound.
- **Server state**: TanStack Query v5 through `openapi-react-query` (ADR 0002).
- **UI kit**: **shadcn/ui** (components copied into `web/src/components/ui/`, Radix primitives underneath → good a11y baseline) + **Tailwind CSS v4**. Theme tokens as CSS variables; run-type colours defined as tokens in M3.
- **i18n**: `i18next` + `react-i18next`, English resources in `web/src/i18n/en/*.json` from M1 (the shell already has user-facing strings); Polish added later.
- **Forms** (from M3): react-hook-form + zod. **Charts** (M5): Recharts. Not installed in M1.
- **Quality**: ESLint flat config (`typescript-eslint`, `eslint-plugin-react-hooks`, `eslint-plugin-react-refresh`, `eslint-plugin-jsx-a11y`), Prettier; Vitest + React Testing Library + jsdom; Playwright for E2E.
- **Folder layout**: `src/app/` (providers, router), `src/components/{ui,layout}/`, `src/features/<area>/`, `src/api/` (`client.ts` + `generated/`), `src/i18n/`, `e2e/`.

## Consequences

- shadcn components are owned code: upgrades are manual but nothing breaks from upstream.
- React 19.2.x + RR v8 + Tailwind v4 are current majors — fewer migrations ahead, but some third-party libs may lag (check peer deps when adding).
- Data mode leaves room for loaders/`lazy` route splitting later without rewriting the router.

## Alternatives considered

- **Mantine / MUI / Chakra** — complete kits, but heavier runtime styling and less control over calendar-grid markup; shadcn + Tailwind is what the brief prefers.
- **React Router declarative mode** (`<BrowserRouter>`) — simpler, but no route-level error boundaries/lazy routes; data mode costs ~nothing extra.
- **TanStack Router** — excellent type-safe routing, but the brief names React Router and v8 is well supported.
- **React Router framework mode** — brings a server/SSR build we don't need for a static SPA.

## References

- React Router v8 announcement: https://remix.run/blog/react-router-v8.md
- React Router modes overview (secondary, 2026): https://scrimba.com/articles/react-router-complete-guide-2026/
- shadcn/ui with Vite (Tailwind v4 via `@tailwindcss/vite`): https://ui.shadcn.com/docs/installation/vite
