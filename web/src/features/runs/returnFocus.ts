/**
 * Nested focus return (AC13). Panels are sibling routes (day -> details -> edit/rate), so closing one with
 * `navigate(-1)` *remounts* the previous dialog, whose first-tabbable default would otherwise win over "the link that
 * opened the thing I just closed".
 *
 * - The action link carries `state={returnFocusState(selector)}`: the selector of that link inside the dialog it is in.
 * - Closing the panel (`useClosePanel`) hands that selector over with `handOverReturnFocus`.
 * - The dialog that mounts next takes it in `onOpenAutoFocus` (`takeReturnFocusTarget`) and focuses the element.
 */
export interface ReturnFocusState {
  returnFocus: string
}

export const returnFocusState = (selector: string): ReturnFocusState => ({ returnFocus: selector })

export function readReturnFocus(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('returnFocus' in state)) return null
  return typeof state.returnFocus === 'string' && state.returnFocus.length > 0 ? state.returnFocus : null
}

let pending: string | null = null

export function handOverReturnFocus(selector: string | null): void {
  pending = selector
}

/** The element to focus in the dialog that just opened, if a closing panel asked for one and it exists. */
export function takeReturnFocusTarget(root: ParentNode = document): HTMLElement | null {
  const selector = pending
  pending = null
  if (!selector) return null
  try {
    return root.querySelector<HTMLElement>(selector)
  } catch {
    return null // an invalid selector must never break opening a dialog
  }
}
