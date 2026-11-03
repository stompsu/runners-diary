import { X } from 'lucide-react'
import type { ReactNode, RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { Dialog as DialogPrimitive } from 'radix-ui'
import { Button } from '@/components/ui/button'
import { DialogDescription, DialogOverlay, DialogPortal, DialogTitle } from '@/components/ui/dialog'
import { focusDayCell } from '@/features/calendar/focusCell'
import { takeReturnFocusTarget } from '@/features/runs/returnFocus'
import { useIsPhone } from '@/hooks/useIsPhone'
import { cn } from '@/lib/utils'

export interface ResponsiveDialogProps {
  title: string
  description?: string
  /** Close request (X button, Escape, overlay click). Panels are URL-driven, so this navigates. */
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  size?: 'md' | 'lg'
  /** The grid day that gets the focus back when the dialog goes away (AC13). */
  restoreFocusDate?: string | null
  /** Element to focus when the dialog opens instead of the first tabbable one. */
  initialFocus?: RefObject<HTMLElement | null>
  /** Marks the dialog for tests and E2E (`data-panel`). */
  panel?: string
}

/**
 * A centred dialog from `sm` up and a bottom sheet on phones (ADR 0016). Both are the same modal Radix dialog, so
 * focus trap, Escape, labelling and `aria-modal` behave identically; only the placement classes differ.
 * The dialog is "open" for as long as its route is mounted: leaving the route closes it.
 */
export function ResponsiveDialog({
  title,
  description,
  onClose,
  children,
  footer,
  size = 'md',
  restoreFocusDate,
  initialFocus,
  panel,
}: ResponsiveDialogProps) {
  const { t } = useTranslation()
  const phone = useIsPhone()

  return (
    <DialogPrimitive.Root open onOpenChange={(open) => (open ? undefined : onClose())}>
      <DialogPortal>
        <DialogOverlay />
        <DialogPrimitive.Content
          data-panel={panel}
          data-variant={phone ? 'sheet' : 'dialog'}
          {...(description ? {} : { 'aria-describedby': undefined })}
          onOpenAutoFocus={(event) => {
            // A dialog revealed by closing a nested one gets the focus on the link that had opened the closed one.
            const target = takeReturnFocusTarget() ?? initialFocus?.current
            if (target) {
              event.preventDefault()
              target.focus()
            }
          }}
          onCloseAutoFocus={(event) => {
            // The grid stays mounted behind the route outlet; return to the day that opened this (AC13).
            // When another dialog took over (day -> details), that one owns the focus.
            event.preventDefault()
            if (document.querySelector('[role="dialog"], [role="alertdialog"]')) return
            focusDayCell(restoreFocusDate)
          }}
          className={cn(
            'fixed z-50 flex flex-col bg-background shadow-lg outline-none',
            'data-[state=closed]:animate-out data-[state=open]:animate-in',
            phone
              ? 'inset-x-0 bottom-0 max-h-[92dvh] rounded-t-xl border-t data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom'
              : cn(
                  'top-1/2 left-1/2 max-h-[90dvh] w-[calc(100%-2rem)] -translate-x-1/2 -translate-y-1/2 rounded-lg border data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95',
                  size === 'lg' ? 'max-w-2xl' : 'max-w-lg',
                ),
          )}
        >
          <div className="flex items-start justify-between gap-2 border-b p-4">
            <div className="min-w-0">
              <DialogTitle tabIndex={-1} className="text-lg font-semibold outline-none">
                {title}
              </DialogTitle>
              {description ? <DialogDescription className="mt-1">{description}</DialogDescription> : null}
            </div>
            <DialogPrimitive.Close asChild>
              <Button type="button" variant="ghost" size="icon" className="-mt-1 -mr-1 size-11 sm:size-9">
                <X aria-hidden="true" />
                <span className="sr-only">{t('close')}</span>
              </Button>
            </DialogPrimitive.Close>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-4">{children}</div>
          {footer ? <div className="flex flex-wrap justify-end gap-2 border-t p-4">{footer}</div> : null}
        </DialogPrimitive.Content>
      </DialogPortal>
    </DialogPrimitive.Root>
  )
}
