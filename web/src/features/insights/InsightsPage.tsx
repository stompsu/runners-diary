import { useTranslation } from 'react-i18next'

export function InsightsPage() {
  const { t } = useTranslation()

  return (
    <section aria-labelledby="insights-title">
      <h1 id="insights-title" className="text-2xl font-semibold">
        {t('insights.title')}
      </h1>
      <p className="mt-2 text-muted-foreground">{t('insights.placeholder')}</p>
    </section>
  )
}
