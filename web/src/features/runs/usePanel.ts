import { useCallback } from 'react'
import { useLocation, useNavigate, useNavigationType, useParams } from 'react-router'
import { handOverReturnFocus, readReturnFocus } from './returnFocus'
import { diaryPath, parseDiaryMonth, type DiaryMonth } from '@/features/diary/diaryMonth'

/** The month of the diary route that hosts the panel (the parent already rejected invalid months). */
export function usePanelMonth(): DiaryMonth {
  const params = useParams()
  const month = parseDiaryMonth(params.year, params.month)
  if (!month) throw new Error('Panels render only below a valid /diary/:year/:month route')
  return month
}

/**
 * Closing a panel goes back when the panel was opened from inside the app (so Back and Close agree and the history
 * stays tidy), and replaces itself with the month when it was opened by a deep link or after a reload.
 */
export function useClosePanel(): () => void {
  const navigate = useNavigate()
  const navigationType = useNavigationType()
  const returnFocus = readReturnFocus(useLocation().state)
  const month = usePanelMonth()
  const base = diaryPath(month)
  return useCallback(() => {
    if (navigationType === 'PUSH') {
      // Going back remounts the dialog this one was opened from; tell it which link to focus.
      handOverReturnFocus(returnFocus)
      void navigate(-1)
    } else {
      handOverReturnFocus(null)
      void navigate(base, { replace: true })
    }
  }, [navigate, navigationType, base, returnFocus])
}
