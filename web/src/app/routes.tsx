import type { RouteObject } from 'react-router'
import { RequireAuth } from '@/auth/RequireAuth'
import { AppShell } from '@/components/layout/AppShell'
import { DiaryMonthPage } from '@/features/calendar/DiaryMonthPage'
import { RedirectToCurrentMonth } from '@/features/diary/RedirectToCurrentMonth'
import { NotFoundPage } from '@/features/errors/NotFoundPage'
import { RouteErrorPage } from '@/features/errors/RouteErrorPage'
import { SignInPage } from '@/features/auth/SignInPage'
import { InsightsPage } from '@/features/insights/InsightsPage'
import { panelRoutes } from '@/features/runs/panelRoutes'

// The URL is the source of truth for navigation state (brief §1.1.2): /diary/2026/11.
// Everything except /signin sits behind RequireAuth (ADR 0014 §7).
export const routes: RouteObject[] = [
  { path: 'signin', element: <SignInPage />, errorElement: <RouteErrorPage /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <AppShell />,
        errorElement: <RouteErrorPage />,
        children: [
          { index: true, element: <RedirectToCurrentMonth /> },
          { path: 'diary', element: <RedirectToCurrentMonth /> },
          {
            path: 'diary/:year/:month',
            element: <DiaryMonthPage />,
            // Panels are URL-addressable and lazy-loaded (ADR 0016).
            children: panelRoutes,
          },
          { path: 'insights', element: <InsightsPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]
