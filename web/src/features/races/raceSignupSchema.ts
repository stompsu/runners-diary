import { z } from 'zod'
import { parseDuration } from '@/lib/format/duration'

export interface RaceSignupFormValues {
  /** The chosen distance in metres as text ('' = none); radio values are strings. */
  distanceMeters: string
  /** `HH:mm` or '' */
  startTime: string
  /** h:mm:ss or '' */
  targetTime: string
}

export const MAX_TARGET_SECONDS = 604_800

/** Messages are i18n keys under `races:signup.validation.*`, resolved where they are shown. */
export function raceSignupSchema(needsDistance: boolean) {
  return z
    .object({ distanceMeters: z.string(), startTime: z.string(), targetTime: z.string() })
    .superRefine((values, ctx) => {
      if (needsDistance && values.distanceMeters === '') {
        ctx.addIssue({ code: 'custom', path: ['distanceMeters'], message: 'distanceRequired' })
      }
      if (values.startTime !== '' && !/^([01]\d|2[0-3]):[0-5]\d$/.test(values.startTime)) {
        ctx.addIssue({ code: 'custom', path: ['startTime'], message: 'startTime' })
      }
      if (values.targetTime.trim() !== '') {
        const seconds = parseDuration(values.targetTime)
        if (seconds === null || seconds < 1 || seconds > MAX_TARGET_SECONDS) {
          ctx.addIssue({ code: 'custom', path: ['targetTime'], message: 'targetTime' })
        }
      }
    })
}

/** The request body: the chosen (or only) distance, the optional start time and target time. */
export function toSignupRequest(values: RaceSignupFormValues, onlyDistance: number | null) {
  const target = values.targetTime.trim() === '' ? null : parseDuration(values.targetTime)
  return {
    distanceMeters: values.distanceMeters === '' ? onlyDistance : Number(values.distanceMeters),
    startTime: values.startTime === '' ? null : values.startTime,
    targetTimeSeconds: target,
  }
}
