import { Navigate } from 'react-router'
import { currentDiaryMonth, diaryPath } from './diaryMonth'

export function RedirectToCurrentMonth() {
  return <Navigate to={diaryPath(currentDiaryMonth())} replace />
}
