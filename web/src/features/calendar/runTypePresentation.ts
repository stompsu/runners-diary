import type { CSSProperties } from 'react'
import { Bike, Circle, Footprints, Gauge, Moon, Route, TreePine, Trophy, Zap, type LucideIcon } from 'lucide-react'
import type runTypeLabels from '@/i18n/en/runTypes.json'

/** Plan §3.6: ~8 colour families cover the 20 run types; each has a `--run-<family>` token in index.css and an icon. */
export type RunFamily = 'race' | 'easy' | 'long' | 'quality' | 'trail' | 'treadmill' | 'cross' | 'rest' | 'unknown'

export type RunTypeCode = Exclude<keyof typeof runTypeLabels, 'unknown'>

// Record<RunTypeCode, …> makes the compiler fail when a label exists without a family (AC15).
const FAMILY_BY_CODE: Record<RunTypeCode, RunFamily> = {
  roadRace: 'race',
  trailRace: 'race',
  ultra: 'race',
  trackRace: 'race',
  crossCountry: 'race',
  virtualRace: 'race',
  easy: 'easy',
  recovery: 'easy',
  long: 'long',
  tempo: 'quality',
  intervals: 'quality',
  fartlek: 'quality',
  hillRepeats: 'quality',
  progression: 'quality',
  racePace: 'quality',
  stridesDrills: 'quality',
  trailTraining: 'trail',
  treadmill: 'treadmill',
  crossTraining: 'cross',
  rest: 'rest',
}

export const FAMILY_ICON: Record<RunFamily, LucideIcon> = {
  race: Trophy,
  easy: Footprints,
  long: Route,
  quality: Zap,
  trail: TreePine,
  treadmill: Gauge,
  cross: Bike,
  rest: Moon,
  unknown: Circle,
}

export function isKnownRunType(code: string | undefined): code is RunTypeCode {
  return code !== undefined && Object.hasOwn(FAMILY_BY_CODE, code)
}

/** Codes this client does not know (a newer API) fall back to a neutral family and the "Other" label. */
export function familyOf(code: string | undefined): RunFamily {
  return isKnownRunType(code) ? FAMILY_BY_CODE[code] : 'unknown'
}

export function runTypeLabelKey(code: string | undefined): RunTypeCode | 'unknown' {
  return isKnownRunType(code) ? code : 'unknown'
}

/** Routes the family token through one custom property, so Tailwind sees static class names (`bg-(--run)`). */
export function familyStyle(code: string | undefined): CSSProperties {
  return { '--run': `var(--run-${familyOf(code)})` } as CSSProperties
}
