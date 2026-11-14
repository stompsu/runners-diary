/** Preset ranges in km; Ultra starts just above a marathon (ADR 0037: "ultra" is anything longer). */
export const DISTANCE_PRESETS = [
  { key: 'fiveK', min: '4.5', max: '5.5' },
  { key: 'tenK', min: '9.5', max: '10.5' },
  { key: 'half', min: '21', max: '21.5' },
  { key: 'marathon', min: '42', max: '42.5' },
  { key: 'ultra', min: '42.2', max: '250' },
] as const
