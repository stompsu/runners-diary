import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { NotFoundPage } from '@/features/errors/NotFoundPage'
import { parseDiaryMonth } from './diaryMonth'

export function DiaryPage() {
  const { t, i18n } = useTranslation()
  const params = useParams()
  const diaryMonth = parseDiaryMonth(params.year, params.month)

  if (!diaryMonth) return <NotFoundPage />

  const monthLabel = new Intl.DateTimeFormat(i18n.language, { month: 'long', year: 'numeric' }).format(
    new Date(diaryMonth.year, diaryMonth.month - 1, 1),
  )

  return (
    <section aria-labelledby="diary-title">
      <h1 id="diary-title" className="text-2xl font-semibold">
        {t('diary.title', { month: monthLabel })}
      </h1>
      <p className="mt-2 text-muted-foreground">{t('diary.placeholder')}</p>
    </section>
  )
}
