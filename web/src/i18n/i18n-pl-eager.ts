/**
 * The Polish counterparts of the eager English namespaces, as a dynamic-import target (ADR 0042 §4): a lazy chunk
 * that only Polish users download (`i18n-pl-eager` is in LAZY_ONLY of scripts/bundle-budget.mjs). The glob picks up
 * whichever files exist, so Part B only adds JSON; the parity test requires all of them once `pl` is released.
 */
const modules = import.meta.glob<Record<string, unknown>>('./pl/{common,auth,calendar,runs,runTypes,errors}.json', {
  eager: true,
  import: 'default',
})

export const polishEager: Record<string, Record<string, unknown>> = Object.fromEntries(
  Object.entries(modules).map(([path, bundle]) => [/\/([^/]+)\.json$/.exec(path)![1]!, bundle]),
)
