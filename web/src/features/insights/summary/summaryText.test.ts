import i18n from 'i18next'
import { describe, expect, it } from 'vitest'
import '@/i18n/lazyBundles'
import type { SummaryValue } from '../insightsTypes'
import { formatSummaryValue, type SummaryFormatContext } from './summaryFormat'
import { renderSummaryText } from './summaryText'

const ctx: SummaryFormatContext = {
  locale: 'en',
  t: i18n.getFixedT('en', 'insights'),
  tType: i18n.getFixedT('en', 'runTypes'),
}
const fmt = (key: string, value: SummaryValue) => formatSummaryValue(key, value, ctx)
const num = (number: number, unit: SummaryValue['unit']): SummaryValue => ({ number, text: null, unit })
const txt = (text: string, unit: SummaryValue['unit']): SummaryValue => ({ number: null, text, unit })
const render = (text: string, values: Record<string, SummaryValue> = {}) => renderSummaryText(text, values, fmt)

describe('formatSummaryValue (every unit, with the dashboard helpers)', () => {
  it.each<[string, string, SummaryValue, string]>([
    ['distance', 'totals.distance', num(382_600, 'meters'), '382.6 km'],
    ['elevation', 'totals.elevationGain', num(2594, 'meters'), '2,594 m'],
    ['signed distance delta', 'comparison.delta.distance.absolute', num(47_700, 'meters'), '+47.7 km'],
    ['negative elevation delta', 'comparison.delta.elevationGain.absolute', num(-904, 'meters'), '-904 m'],
    ['duration', 'totals.duration', num(134_412, 'seconds'), '37:20:12'],
    ['race time', 'race.1.resultTime', num(5432, 'seconds'), '1:30:32'],
    ['duration delta', 'comparison.delta.duration.absolute', num(-300, 'seconds'), '-0:05:00'],
    ['load', 'totals.load', num(11_677.4, 'load'), '11,677 AU'],
    ['load delta', 'comparison.delta.load.absolute', num(1200, 'load'), '+1,200 AU'],
    ['ratio', 'acwr.peak.value', num(1.4, 'ratio'), '1.40'],
    ['percent', 'comparison.delta.distance.percent', num(14.2, 'percent'), '+14.2%'],
    ['negative percent', 'comparison.delta.load.percent', num(-3.55, 'percent'), '-3.6%'],
    ['share', 'dq.estimatedLoadShare', num(0.18, 'share'), '18%'],
    ['pace', 'pace.last.pace', num(351.3, 'secondsPerKm'), '5:51 /km'],
    ['pace rounding', 'pace.first.pace', num(359.6, 'secondsPerKm'), '6:00 /km'],
    ['pace slope', 'pace.slope', num(-2.46, 'secondsPerKmPerWeek'), '-2.5 s/km per week'],
    ['count', 'totals.runCount', num(1234, 'count'), '1,234'],
    ['count delta', 'comparison.delta.runCount.absolute', num(4, 'count'), '+4'],
    ['one day', 'planned.upcoming', num(1, 'days'), '1 day'],
    ['days', 'dq.days', num(12, 'days'), '12 days'],
    ['days delta', 'comparison.delta.activeDays.absolute', num(-2, 'days'), '-2 days'],
    ['date', 'race.1.date', txt('2026-10-11', 'date'), 'Oct 11, 2026'],
    ['band', 'acwr.peak.band', txt('high', 'band'), 'High'],
    ['run type', 'week.9.raceType', txt('trailRace', 'runType'), 'Trail race'],
    ['unknown run type', 'week.9.raceType', txt('hovercraft', 'runType'), 'Other'],
    ['race category', 'race.1.category', txt('halfMarathon', 'raceCategory'), 'half marathon'],
    ['race status', 'race.1.status', txt('completed', 'raceStatus'), 'completed'],
    ['flag yes', 'race.1.isPersonalBest', txt('true', 'flag'), 'yes'],
    ['flag no', 'acwr.peak.isRaceWeek', txt('false', 'flag'), 'no'],
    ['stars', 'race.1.stars', num(4, 'stars'), '4 of 5'],
  ])('%s', (_name, key, value, expected) => {
    expect(fmt(key, value)).toBe(expected)
  })

  it.each<[string, SummaryValue]>([
    ['a number unit without a number', txt('x', 'meters')],
    ['an invalid date', txt('2026-13-45', 'date')],
    ['an unknown band', txt('extreme', 'band')],
    ['an unknown category', txt('ultraMarathon', 'raceCategory')],
    ['a flag that is not a boolean', txt('maybe', 'flag')],
    ['a non-finite number', num(Number.NaN, 'ratio')],
    ['a unit this client does not know', { number: 1, text: null, unit: 'parsecs' as SummaryValue['unit'] }],
  ])('returns null for %s', (_name, value) => {
    expect(fmt('some.key', value)).toBeNull()
  })
})

describe('renderSummaryText', () => {
  const values = {
    'totals.distance': num(382_600, 'meters'),
    'totals.runCount': num(39, 'count'),
    'race.1.date': txt('2026-10-11', 'date'),
  }

  it('replaces every placeholder with its formatted value', () => {
    expect(render('You ran {totals.distance} in {totals.runCount} runs; the race was on {race.1.date}.', values)).toBe(
      'You ran 382.6 km in 39 runs; the race was on Oct 11, 2026.',
    )
  })

  it('uses a value twice', () => {
    expect(render('{totals.runCount} runs, yes, {totals.runCount}.', values)).toBe('39 runs, yes, 39.')
  })

  it('renders a dash for an unknown key', () => {
    expect(render('You reached {no.such.key} in {totals.runCount} runs.', values)).toBe('You reached — in 39 runs.')
    expect(render('Peak was {no.such.key}, then rest.', values)).toBe('Peak was —, then rest.')
  })

  it('renders a dash for a value that cannot be formatted', () => {
    expect(render('Rated {x.bad}.', { 'x.bad': txt('nope', 'meters') })).toBe('Rated —.')
  })

  it('does not resolve keys through the prototype chain', () => {
    expect(render('A {constructor} B {__proto__} C {toString} D {hasOwnProperty}', values)).toBe('A — B — C — D —')
  })

  it.each([
    ['a stray opening brace', 'Open { here', 'Open here'],
    ['a stray closing brace', 'Close } here', 'Close here'],
    ['nested braces', 'Nested {{totals.runCount}} done', 'Nested 39 done'],
    ['an empty token', 'Empty {} token', 'Empty — token'],
    ['a token with a space', 'Odd {totals runCount} key', 'Odd — key'],
    ['an unterminated token', 'Cut {totals.runCount', 'Cut totals.runCount'],
  ])('never shows raw braces: %s', (_name, text, expected) => {
    const out = render(text, values)
    expect(out).toBe(expected)
    expect(out).not.toMatch(/[{}]/)
  })

  it('keeps markup as text (it is rendered in a text node, never parsed)', () => {
    const attack = '<img src=x onerror=alert(1)> <script>alert(2)</script> {totals.runCount}'
    expect(render(attack, values)).toBe('<img src=x onerror=alert(1)> <script>alert(2)</script> 39')
  })

  it('does not interpret a malicious value either', () => {
    const evil = { 'totals.runCount': txt('<b onmouseover=alert(1)>', 'band') }
    expect(render('Count {totals.runCount}.', evil)).toBe('Count —.')
  })
})
