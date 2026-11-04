import type { components } from '@/api/generated/schema'

type S = components['schemas']

export type InsightsReport = S['InsightsReportResponse']
export type InsightsWeek = S['InsightsWeekDto']
export type InsightsMonth = S['InsightsMonthDto']
export type PeriodTotals = S['PeriodTotalsDto']
export type Deltas = S['DeltasDto']
export type Delta = S['DeltaDto']
export type AcwrPoint = S['AcwrPointDto']
export type DailyLoad = S['DailyLoadDto']
export type RaceResult = S['RaceResultDto']
export type TypeShare = S['TypeShareDto']
export type PaceTrend = S['PaceTrendDto']
export type DataQuality = S['DataQualityDto']
export type Band = NonNullable<S['BandDto']>

/** The API types `compare` as a plain string; the client only ever sends one of these. */
export type CompareMode = 'none' | 'previous' | 'previousYear'

export type SummaryResponse = S['InsightsSummaryResponse']
export type SummaryContent = S['InsightsSummaryDto']
export type SummaryText = S['SummaryTextDto']
export type SummaryValue = S['SummaryValueDto']
export type SummaryGeneration = S['SummaryGenerationDto']
