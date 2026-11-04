import { Search } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { $api } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { writeProblemMessage } from './runProblemMessage'
import { classifyTrackProblem } from '@/features/tracks/trackProblem'
import type { PlaceDto } from '@/features/tracks/trackTypes'
import '@/i18n/lazyBundles'

const MIN_QUERY = 2
const MAX_QUERY = 100
const TEN_MINUTES_MS = 10 * 60 * 1000

export interface PlaceSearchProps {
  onSelect: (place: PlaceDto) => void
  /** Bias for the results (the current pin or the picker centre), when known. */
  near?: { latitude: number; longitude: number } | null
}

/**
 * Place search (ADR 0022): rendered only when `client-config.maps.geocodingEnabled`. The search runs when the user
 * asks for it (button or Enter), never on every keystroke. Results are plain buttons, so Tab / Enter work.
 * It is used inside the run form, so it must not be a `<form>`: Enter is handled here and never submits the run.
 */
export function PlaceSearch({ onSelect, near }: PlaceSearchProps) {
  const { t } = useTranslation('maps')
  const { t: tErrors } = useTranslation('errors')
  const id = useId()
  const [text, setText] = useState('')
  const [submitted, setSubmitted] = useState<string | null>(null)
  const [tooShort, setTooShort] = useState(false)

  const search = $api.useQuery(
    'get',
    '/api/geocoding/search',
    { params: { query: { q: submitted ?? '', lat: near?.latitude, lon: near?.longitude } } },
    { enabled: submitted !== null, staleTime: TEN_MINUTES_MS, retry: false },
  )

  function run() {
    const q = text.trim()
    if (q.length < MIN_QUERY) {
      setTooShort(true)
      setSubmitted(null)
      return
    }
    setTooShort(false)
    setSubmitted(q)
  }

  let status: string | null = null
  if (submitted !== null) {
    if (search.isFetching) status = t('search.searching')
    else if (search.isError) {
      const problem = classifyTrackProblem(search.error)
      status =
        problem.kind === 'featureUnavailable'
          ? t('search.unavailable')
          : problem.kind === 'other'
            ? problem.problem.kind === 'validation'
              ? t('search.tooShort')
              : writeProblemMessage(problem.problem, tErrors)
            : t('search.failed')
    } else if (search.data) {
      status =
        search.data.results.length === 0 ? t('search.none') : t('search.found', { count: search.data.results.length })
    }
  }

  const results = !search.isFetching && !search.isError ? (search.data?.results ?? []) : []

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{t('search.label')}</Label>
      <div className="flex gap-2">
        <Input
          id={id}
          type="search"
          value={text}
          maxLength={MAX_QUERY}
          aria-invalid={tooShort ? true : undefined}
          aria-describedby={`${id}-hint`}
          className="h-11 sm:h-9"
          onChange={(event) => {
            setText(event.target.value)
            setTooShort(false)
          }}
          onKeyDown={(event) => {
            if (event.key !== 'Enter') return
            event.preventDefault()
            run()
          }}
        />
        <Button type="button" variant="outline" className="h-11 sm:h-9" onClick={run}>
          <Search aria-hidden="true" />
          {t('search.button')}
        </Button>
      </div>
      <p id={`${id}-hint`} className={tooShort ? 'text-sm text-destructive' : 'text-sm text-muted-foreground'}>
        {tooShort ? t('search.tooShort') : t('search.hint')}
      </p>
      <output className="block min-h-5 text-sm text-muted-foreground">{status}</output>
      {results.length > 0 ? (
        <ul aria-label={t('search.results')} className="flex flex-col gap-1">
          {results.map((place) => (
            <li key={`${place.name}-${place.latitude}-${place.longitude}`}>
              <Button
                type="button"
                variant="outline"
                className="h-auto min-h-11 w-full flex-col items-start gap-0 py-2 whitespace-normal sm:min-h-9"
                onClick={() => {
                  onSelect(place)
                  setSubmitted(null)
                  setText('')
                }}
              >
                <span className="font-medium">{place.name}</span>
                {place.type ? <span className="text-xs text-muted-foreground">{place.type}</span> : null}
              </Button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  )
}
