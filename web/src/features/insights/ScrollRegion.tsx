/* oxlint-disable jsx-a11y/no-noninteractive-tabindex --
   A horizontally or vertically scrolling table container must be reachable with the keyboard (axe
   scrollable-region-focusable); a labelled <section> with tabindex=0 is the documented pattern. */
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** A labelled, keyboard-focusable scroll container: wide tables scroll inside it, never the page. */
export function ScrollRegion({
  label,
  className,
  children,
}: {
  label: string
  className?: string
  children: ReactNode
}) {
  return (
    <section
      tabIndex={0}
      aria-label={label}
      className={cn(
        'overflow-auto rounded-md border focus-visible:outline-2 focus-visible:outline-offset-2',
        className,
      )}
    >
      {children}
    </section>
  )
}
