import { useId, type ReactNode } from 'react'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'

/** A labelled checkbox row with a phone-sized tap target; the whole row is the label. */
export function CheckRow({
  label,
  checked,
  onChange,
  hint,
}: {
  label: ReactNode
  checked: boolean
  onChange: (checked: boolean) => void
  hint?: string
}) {
  const id = useId()
  const hintId = `${id}-hint`
  return (
    <div className="flex flex-col gap-1">
      <Label
        htmlFor={id}
        className="flex min-h-11 cursor-pointer items-center gap-3 rounded-md border px-3 font-normal has-data-[state=checked]:border-primary has-data-[state=checked]:bg-primary/5 sm:min-h-9"
      >
        <Checkbox
          id={id}
          checked={checked}
          aria-describedby={hint ? hintId : undefined}
          onCheckedChange={(next) => onChange(next === true)}
        />
        {label}
      </Label>
      {hint ? (
        <p id={hintId} className="px-1 text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  )
}
