import { useTranslation } from 'react-i18next'
import { NavLink, Outlet } from 'react-router'
import { useClientConfig } from '@/auth/useClientConfig'
import { useTimeZoneSync } from '@/auth/useTimeZoneSync'
import { Toaster } from '@/components/ui/sonner'
import { cn } from '@/lib/utils'
import { ApiStatus } from './ApiStatus'
import { DemoBanner } from './DemoBanner'
import { UserMenu } from './UserMenu'

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  cn(
    'rounded-md px-3 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2',
    isActive ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-accent',
  )

export function AppShell() {
  const { t } = useTranslation()
  useTimeZoneSync()
  // Settings only hold race interests so far (M8): hidden while race proposals are switched off.
  const racesEnabled = useClientConfig().data?.races.enabled === true

  return (
    <div className="min-h-dvh bg-background text-foreground">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:p-2">
        {t('app.skipToContent')}
      </a>
      <DemoBanner />
      <header className="border-b">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-2 px-4 py-3">
          <span className="text-lg font-semibold">{t('app.name')}</span>
          <nav aria-label={t('nav.label')} className="flex gap-1">
            <NavLink to="/diary" className={navLinkClass}>
              {t('nav.diary')}
            </NavLink>
            <NavLink to="/insights" className={navLinkClass}>
              {t('nav.insights')}
            </NavLink>
            {racesEnabled ? (
              <NavLink to="/settings" className={navLinkClass}>
                {t('nav.settings')}
              </NavLink>
            ) : null}
          </nav>
          <div className="flex items-center gap-3">
            <ApiStatus />
            <UserMenu />
          </div>
        </div>
      </header>
      <main id="main" className="mx-auto max-w-6xl px-4 py-6">
        <Outlet />
      </main>
      <Toaster position="bottom-center" />
    </div>
  )
}
