let cached: boolean | undefined

/** MapLibre GL JS v6 needs WebGL2 (ADR 0022). Probed once; a throw or a missing context counts as "no". */
export function supportsWebGl2(): boolean {
  if (cached !== undefined) return cached
  try {
    const canvas = document.createElement('canvas')
    const gl = canvas.getContext('webgl2')
    cached = gl !== null && gl !== undefined
    gl?.getExtension('WEBGL_lose_context')?.loseContext()
  } catch {
    cached = false
  }
  return cached
}
