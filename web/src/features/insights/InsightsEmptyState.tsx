import { CalendarPlus } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { diaryPath } from '@/features/diary/diaryMonth'
import { monthOf, type PlainDate } from '@/lib/dates/plainDate'
import { useInsightsT } from './useInsightsT'

/** No completed sessions in the period: say so and point to the diary, in the month the period ends in. */
export function InsightsEmptyState({ diaryDate }: { diaryDate: PlainDate }) {
  const { t } = useInsightsT()

  return (
    <section
      aria-labelledby="insights-empty-title"
      className="flex flex-col items-start gap-3 rounded-lg border border-dashed p-6"
      data-testid="insights-empty"
    >
      <h2 id="insights-empty-title" className="text-lg font-semibold">
        {t('empty.title')}
      </h2>
      <p className="text-muted-foreground">{t('empty.body')}</p>
      <Button asChild>
        <Link to={diaryPath(monthOf(diaryDate))}>
          <CalendarPlus aria-hidden="true" />
          {t('empty.addRun')}
        </Link>
      </Button>
    </section>
  )
}
