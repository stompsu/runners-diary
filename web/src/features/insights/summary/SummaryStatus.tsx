/**
 * The polite live region of the card (M6 AC12). It is always mounted, so a change of its text is announced; errors use
 * `role="alert"` elsewhere. Visually hidden: the generating state is also visible in the card.
 */
export function SummaryStatus({ message }: { message: string }) {
  return (
    <output aria-live="polite" aria-atomic="true" className="sr-only" data-testid="summary-status">
      {message}
    </output>
  )
}
