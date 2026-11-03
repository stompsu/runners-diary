import { Navigate } from 'react-router'
import { useMe } from '@/auth/useMe'
import { QueryError } from '@/features/errors/QueryError'
import { SlowStartNotice } from '@/features/auth/SlowStartNotice'
import { currentDiaryMonth, diaryPath } from './diaryMonth'

/** `/` and `/diary` go to the month of `me.today` (the user's zone, not the browser clock). */
export function RedirectToCurrentMonth() {
  const me = useMe()

  if (me.data) return <Navigate to={diaryPath(currentDiaryMonth(me.data))} replace />
  if (me.isError) return <QueryError error={me.error} onRetry={() => void me.refetch()} />
  return <SlowStartNotice pending />
}
