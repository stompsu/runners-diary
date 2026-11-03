import type { RouteObject } from 'react-router'
import { AppShell } from '@/components/layout/AppShell'
import { DiaryPage } from '@/features/diary/DiaryPage'
import { RedirectToCurrentMonth } from '@/features/diary/RedirectToCurrentMonth'
import { NotFoundPage } from '@/features/errors/NotFoundPage'
import { RouteErrorPage } from '@/features/errors/RouteErrorPage'
import { InsightsPage } from '@/features/insights/InsightsPage'

// The URL is the source of truth for navigation state (brief §1.1.2): /diary/2026/11.
export const routes: RouteObject[] = [
  {
    element: <AppShell />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, element: <RedirectToCurrentMonth /> },
      { path: 'diary', element: <RedirectToCurrentMonth /> },
      { path: 'diary/:year/:month', element: <DiaryPage /> },
      { path: 'insights', element: <InsightsPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]
