import { Star } from 'lucide-react'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { FieldError } from '@/components/ui/field'
import { cn } from '@/lib/utils'

/**
 * Five stars as a native radio group (arrow keys, one tab stop, labelled). The stars are drawn as icons, but the
 * value is always spelled out in text as well, so the rating never relies on shape or colour alone.
 */
export function RatingField({
  value,
  onChange,
  error,
}: {
  value: number
  onChange: (stars: number) => void
  error?: string
}) {
  const { t } = useTranslation('runs')
  const name = useId()
  const labelId = `${name}-label`
  const errorId = `${name}-error`

  return (
    <div className="flex flex-col gap-2">
      <span id={labelId} className="text-sm font-medium">
        {t('rating.label')}
      </span>
      <div className="flex flex-wrap items-center gap-1">
        <div
          role="radiogroup"
          aria-labelledby={labelId}
          aria-describedby={error ? errorId : undefined}
          aria-invalid={error ? true : undefined}
          className="flex"
        >
          {[1, 2, 3, 4, 5].map((stars) => {
            const id = `${name}-${stars}`
            return (
              <span key={stars} className="relative">
                <input
                  id={id}
                  type="radio"
                  name={name}
                  value={stars}
                  checked={value === stars}
                  onChange={() => onChange(stars)}
                  className="peer sr-only"
                />
                <label
                  htmlFor={id}
                  className="flex size-11 cursor-pointer items-center justify-center rounded-md peer-focus-visible:ring-[3px] peer-focus-visible:ring-ring/50 sm:size-9"
                >
                  <Star
                    aria-hidden="true"
                    className={cn('size-6', stars <= value ? 'fill-amber-500 text-amber-600' : 'text-muted-foreground')}
                  />
                  <span className="sr-only">{t('rating.starsOption', { count: stars })}</span>
                </label>
              </span>
            )
          })}
        </div>
        <span className="text-sm text-muted-foreground">
          {value > 0 ? t('rating.selected', { count: value }) : t('rating.none')}
        </span>
        {value > 0 ? (
          <Button type="button" variant="ghost" size="sm" onClick={() => onChange(0)}>
            {t('rating.clear')}
          </Button>
        ) : null}
      </div>
      {error ? <FieldError id={errorId}>{error}</FieldError> : null}
    </div>
  )
}
