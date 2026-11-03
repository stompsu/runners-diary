import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'

export function NotFoundPage() {
  const { t } = useTranslation()

  return (
    <section aria-labelledby="not-found-title">
      <h1 id="not-found-title" className="text-2xl font-semibold">
        {t('errors.notFoundTitle')}
      </h1>
      <p className="mt-2 text-muted-foreground">{t('errors.notFoundBody')}</p>
      <Button asChild className="mt-4">
        <Link to="/diary">{t('errors.goToDiary')}</Link>
      </Button>
    </section>
  )
}
