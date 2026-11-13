/**
 * The 16 voivodeships as ISO 3166-2:PL codes (ADR 0037 §1: `PL-02` ... `PL-32`, the Domain `PolishRegions` table), in
 * code order. The English and Polish names are i18n keys `races:regions.<code>.en` / `.pl`.
 */
export const POLISH_REGION_CODES = [
  'PL-02',
  'PL-04',
  'PL-06',
  'PL-08',
  'PL-10',
  'PL-12',
  'PL-14',
  'PL-16',
  'PL-18',
  'PL-20',
  'PL-22',
  'PL-24',
  'PL-26',
  'PL-28',
  'PL-30',
  'PL-32',
] as const

export type PolishRegionCode = (typeof POLISH_REGION_CODES)[number]
