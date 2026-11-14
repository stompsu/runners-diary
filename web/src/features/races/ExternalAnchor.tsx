import type { ComponentProps } from 'react'

/** An outbound link: new tab, no opener, no referrer (the target is a third party; ADR 0036, AC17). */
export function ExternalAnchor(props: Omit<ComponentProps<'a'>, 'target' | 'rel' | 'referrerPolicy'>) {
  // oxlint-disable-next-line jsx-a11y/anchor-has-content -- the content arrives through `props.children`
  return <a {...props} target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer" />
}
