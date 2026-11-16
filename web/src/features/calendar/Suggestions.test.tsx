import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { typeId, type RunWriteRequest } from '@/test/fakeApi'
import { renderRoute, signInForTest } from '@/test/render'
import { recordRequests } from '@/test/requests'
import { setPhoneViewport } from '@/test/viewport'
import { stubResizeObserver } from '@/test/resizeObserver'
import '@/i18n/lazyBundles'
import { DayCell } from './DayCell'
import { CalendarLegend } from './CalendarLegend'
import { setShowSuggestions } from './suggestionQueries'

beforeEach(() => {
  stubResizeObserver()
  setPhoneViewport(false)
})

// Today 2026-11-03 (Tue). The generated plan puts Tue/Thu/Sat/Sun sessions from Wed 11-04: Thu 11-05 tempo, Sat 11-07
// easy, Sun 11-08 long. The goal (a 10K 70 days out) is on 2027-01-12.
const HISTORY: RunWriteRequest[] = [
  { date: '2026-10-15', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-20', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 8000, durationSeconds: 2700 },
  { date: '2026-10-25', status: 'Completed', runTypeId: typeId('long'), distanceMeters: 16_000, durationSeconds: 5700 },
  { date: '2026-11-07', status: 'Planned', runTypeId: typeId('bodyBar'), title: 'Body Bar' },
]
const cell = (date: string) => document.querySelector<HTMLElement>(`[data-date="${date}"]`)!
const bubbles = (date?: string) =>
  (date ? cell(date) : document).querySelectorAll<HTMLElement>('button[data-kind="suggestion"]')

async function month(options: { plan?: boolean; goal?: boolean } & Parameters<typeof resetFakeApi>[0] = {}) {
  const { plan = true, goal = true, ...fake } = options
  resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY, ...fake })
  const token = signInForTest('guest')
  let goalId = ''
  if (goal) {
    goalId = getFakeApi().addGoal(token, { title: 'Autumn 10K', targetDate: '2026-11-18' }).id
    if (plan) {
      const response = await getFakeApi().handle(
        new Request(`http://localhost/api/goals/${goalId}/plan`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${token}` },
        }),
      )
      if (response.status !== 200) throw new Error(`plan failed: ${response.status}`)
    }
  }
  const user = userEvent.setup()
  const result = renderRoute('/diary/2026/11', { session: 'none' })
  await screen.findByRole('grid', { hidden: true })
  return { token, goalId, user, ...result }
}

describe('Grid: suggestion bubbles', () => {
  it('draws hatched, sparkled bubbles distinct from planned runs by shape and icon', async () => {
    await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    const bubble = bubbles('2026-11-05')[0]!
    expect(bubble).toHaveAttribute('aria-hidden', 'true')
    expect(bubble).toHaveAttribute('data-kind', 'suggestion')
    expect(bubble.style.backgroundImage).toContain('repeating-linear-gradient')
    expect(bubble.className).toContain('border-solid')
    expect(bubble.className).not.toContain('border-dashed')
    expect(bubble.className).not.toContain('border-dotted')
    expect(bubble.querySelector('svg')).not.toBeNull()
    expect(bubble).toHaveTextContent('Tempo')
    expect(bubble).toHaveTextContent('Suggested')
    // The planned run on the 7th keeps its dashed look next to the suggestion.
    const run = cell('2026-11-07').querySelector<HTMLElement>('button[data-run-id]')!
    expect(run.className).toContain('border-dashed')
  })

  it('renders a sparkles glyph for the phone layout and a goal marker on the target date', async () => {
    await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    expect(cell('2026-11-05').querySelector('svg[data-kind="suggestion"]')).not.toBeNull()
    expect(cell('2026-11-18').querySelector('[data-kind="goal"]')).toHaveAttribute('title', 'Goal: Autumn 10K')
    expect(cell('2026-11-19').querySelector('[data-kind="goal"]')).toBeNull()
  })

  it('opens the Day panel from a bubble click', async () => {
    const { user, router } = await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    await user.click(bubbles('2026-11-05')[0]!)
    expect(router.state.location.pathname).toBe('/diary/2026/11/day/2026-11-05')
  })

  it('names suggestions and the goal in the cell, before race proposals', async () => {
    await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    expect(cell('2026-11-05')).toHaveAccessibleName(
      /^Thursday, November 5, 2026, no runs; 1 suggested training: Tempo run, \d+(\.\d+)? km/,
    )
    expect(cell('2026-11-18')).toHaveAccessibleName('Wednesday, November 18, 2026, no runs; goal: Autumn 10K')
    // The planned Body Bar run on the 7th does not block the suggestion (plan A12) and both are named.
    expect(cell('2026-11-07')).toHaveAccessibleName(/1 run: Body Bar.*, Planned; 1 suggested training: Easy run, /)
  })

  it('does not add suggestions to the week totals', async () => {
    await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    const week = document.querySelector<HTMLElement>('tr:has([data-date="2026-11-05"]) th[scope="row"]')!
    // Only the completed 0 km shows; no planned distance appears for the week with suggestions.
    expect(week.textContent).not.toMatch(/Planned/)
  })

  it('offers the legend entries while the layer is on', async () => {
    await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    const legend = screen.getByRole('list', { name: 'Legend', hidden: true })
    for (const name of ['Suggested (hatched, sparkles)', 'Goal (target)', 'From plan']) {
      expect(within(legend).getByText(name)).toBeInTheDocument()
    }
  })

  it('asks nothing and shows nothing while the feature is off', async () => {
    await month({ goalsEnabled: false, plan: false, goal: false })
    expect(getFakeApi().requests.some((r) => r.includes('/api/training-suggestions') || r.includes('/api/goals'))).toBe(
      false,
    )
    expect(screen.queryByTestId('suggestions-toggle')).toBeNull()
    expect(bubbles()).toHaveLength(0)
  })
})

describe('DayCell: order and cap', () => {
  it('shows runs, then suggestions, then proposals, sharing one cap and one "+n"', () => {
    const suggestion = (id: string) =>
      ({ id, goalId: 'g', date: '2026-11-12', runTypeId: 20, status: 'Pending' }) as never
    render(
      <table>
        <tbody>
          <tr>
            <DayCell
              date="2026-11-12"
              inMonth
              isToday={false}
              isTabStop
              isSelected={false}
              runs={[{ id: 'a', status: 'Planned', runTypeId: 20, distanceMeters: 8000, goalId: null } as never]}
              suggestions={[suggestion('s1'), suggestion('s2')]}
              proposals={[
                { raceId: 'p1', name: 'Race', date: '2026-11-12', distancesMeters: [10000], state: 'None' } as never,
              ]}
              codes={new Map([[20, 'easy']])}
              onActivate={() => undefined}
              onOpenDay={() => undefined}
              onOpenRun={() => undefined}
              onOpenProposal={() => undefined}
            />
          </tr>
        </tbody>
      </table>,
    )
    const order = Array.from(
      document.querySelectorAll('button[data-run-id], button[data-kind="suggestion"], button[data-kind="proposal"]'),
    ).map((el) => el.getAttribute('data-kind') ?? 'run')
    expect(order).toEqual(['run', 'suggestion', 'suggestion'])
    expect(screen.getByText('+1')).toBeInTheDocument()
  })

  it('marks a run from a plan with an icon and names it', () => {
    render(
      <table>
        <tbody>
          <tr>
            <DayCell
              date="2026-11-12"
              inMonth
              isToday={false}
              isTabStop
              isSelected={false}
              runs={[{ id: 'a', status: 'Planned', runTypeId: 20, distanceMeters: 8000, goalId: 'g' } as never]}
              proposals={[]}
              codes={new Map([[20, 'easy']])}
              onActivate={() => undefined}
              onOpenDay={() => undefined}
              onOpenRun={() => undefined}
              onOpenProposal={() => undefined}
            />
          </tr>
        </tbody>
      </table>,
    )
    expect(document.querySelector('button[data-run-id]')).toHaveAttribute('data-from-plan', 'true')
    expect(screen.getByRole('gridcell')).toHaveAccessibleName(/Easy run, Planned, 8 km, from plan/)
  })
})

describe('CalendarLegend', () => {
  it('omits the suggestion and goal entries without the layers', () => {
    render(<CalendarLegend showProposals={false} />)
    expect(screen.queryByText('Goal (target)')).toBeNull()
    expect(screen.queryByText('Suggested (hatched, sparkles)')).toBeNull()
  })
})

describe('Suggestions toggle', () => {
  it('hides the bubbles, persists per browser and survives blocked storage', async () => {
    const user = userEvent.setup()
    const { unmount } = await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    const toggle = screen.getByRole('button', { name: 'Show suggestions', hidden: true })
    expect(toggle).toHaveAttribute('aria-pressed', 'true')

    await user.click(toggle)
    await waitFor(() => expect(toggle).toHaveAttribute('aria-pressed', 'false'))
    expect(bubbles()).toHaveLength(0)
    expect(localStorage.getItem('rd.showSuggestions')).toBe('false')
    expect(within(screen.getByRole('list', { name: 'Legend', hidden: true })).queryByText(/Suggested/)).toBeNull()
    // The goal marker is not a bubble: it stays.
    expect(cell('2026-11-18').querySelector('[data-kind="goal"]')).not.toBeNull()
    unmount()

    // A new visit starts with the stored choice and does not ask for the suggestions.
    resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
    signInForTest('guest')
    renderRoute('/diary/2026/11', { session: 'none' })
    await screen.findByRole('grid', { hidden: true })
    expect(screen.getByRole('button', { name: 'Show suggestions', hidden: true })).toHaveAttribute(
      'aria-pressed',
      'false',
    )
    expect(getFakeApi().requests).not.toContain('GET /api/training-suggestions')
  })

  it('keeps working in memory when localStorage throws', async () => {
    const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    const user = userEvent.setup()
    await month()
    const toggle = await screen.findByRole('button', { name: 'Show suggestions', hidden: true })
    await user.click(toggle)
    await waitFor(() => expect(toggle).toHaveAttribute('aria-pressed', 'false'))
    spy.mockRestore()
    setShowSuggestions(true)
  })
})

describe('Day panel', () => {
  async function openDay(date: string) {
    const ctx = await month()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    await ctx.router.navigate(`/diary/2026/11/day/${date}`)
    const dialog = await screen.findByRole('dialog')
    const section = await within(dialog).findByRole('region', { name: 'Suggested trainings' })
    return { ...ctx, dialog, section }
  }

  it('lists the suggestions with three real buttons, even with the toggle off', async () => {
    localStorage.setItem('rd.showSuggestions', 'false')
    resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
    const token = signInForTest('guest')
    const goal = getFakeApi().addGoal(token, { title: 'Autumn 10K', targetDate: '2026-11-18' })
    await getFakeApi().handle(
      new Request(`http://localhost/api/goals/${goal.id}/plan`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      }),
    )
    const { router } = renderRoute('/diary/2026/11/day/2026-11-05', { session: 'none' })
    expect(router).toBeTruthy()
    const section = await screen.findByRole('region', { name: 'Suggested trainings' })
    expect(bubbles()).toHaveLength(0)
    const card = within(section).getByRole('listitem')
    expect(within(card).getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    expect(within(card).getByRole('link', { name: 'Edit & accept' })).toHaveAttribute(
      'href',
      expect.stringMatching(/^\/diary\/2026\/11\/suggestions\/.+\/accept$/),
    )
    expect(within(card).getByRole('button', { name: 'Dismiss' })).toBeInTheDocument()
    expect(card).toHaveTextContent('Tempo')
    expect(card).toHaveTextContent('For Autumn 10K')
  })

  it('accepts: a planned run with the plan link appears, the suggestion goes, totals pick it up', async () => {
    const { user, token, section } = await openDay('2026-11-05')
    const before = getFakeApi().suggestionsOf(token).length
    await user.click(within(section).getByRole('button', { name: 'Accept' }))
    await waitFor(() => expect(getFakeApi().suggestionsOf(token)).toHaveLength(before - 1))
    const run = getFakeApi()
      .runsOf(token)
      .find((r) => r.date === '2026-11-05')!
    expect(run).toMatchObject({ status: 'Planned', goalId: expect.any(String) })
    expect(run.notes).toBeTruthy()
    await waitFor(() =>
      expect(cell('2026-11-05').querySelector('button[data-run-id][data-from-plan="true"]')).not.toBeNull(),
    )
    expect(cell('2026-11-05').querySelector('button[data-kind="suggestion"]')).toBeNull()
    expect(cell('2026-11-05')).toHaveAccessibleName(/Tempo run, Planned, .*, from plan/)
    // Now it is a run, so the week's planned distance counts it.
    await waitFor(() =>
      expect(document.querySelector('tr:has([data-date="2026-11-05"]) th[scope="row"]')?.textContent).toMatch(
        /Planned/,
      ),
    )
  })

  it('dismisses: the bubble goes, the card offers Accept anyway', async () => {
    const { user, token, dialog } = await openDay('2026-11-05')
    await user.click(within(dialog).getByRole('button', { name: 'Dismiss' }))
    await waitFor(() => expect(bubbles('2026-11-05')).toHaveLength(0))
    expect(
      getFakeApi()
        .suggestionsOf(token)
        .find((s) => s.date === '2026-11-05')?.status,
    ).toBe('Dismissed')
    const card = await within(dialog).findByRole('button', { name: 'Accept anyway' })
    expect(within(card.closest('li')!).queryByRole('button', { name: 'Dismiss' })).toBeNull()
    expect(within(card.closest('li')!).queryByRole('link', { name: 'Edit & accept' })).toBeNull()
    await user.click(card)
    await waitFor(() =>
      expect(
        getFakeApi()
          .runsOf(token)
          .some((r) => r.date === '2026-11-05'),
      ).toBe(true),
    )
  })

  it('Edit & accept opens the pre-filled form and sends { run } to the accept endpoint', async () => {
    const { user, token, dialog, router } = await openDay('2026-11-05')
    const suggestion = getFakeApi()
      .suggestionsOf(token)
      .find((s) => s.date === '2026-11-05')!
    const requests = recordRequests()
    await user.click(within(dialog).getByRole('link', { name: 'Edit & accept' }))
    const form = await screen.findByRole('dialog', { name: 'Edit & accept suggestion' })
    expect(router.state.location.pathname).toBe(`/diary/2026/11/suggestions/${suggestion.id}/accept`)
    expect(await within(form).findByLabelText('Distance (km)')).toHaveValue(String(suggestion.distanceMeters! / 1000))
    expect(within(form).getByLabelText('Notes')).toHaveValue(suggestion.description)
    expect(within(form).getByLabelText('Date')).toHaveValue('2026-11-05')
    expect(within(form).getByTestId('pace-hint')).toHaveTextContent('Suggested pace: 5:00 /km to 5:20 /km')

    await user.clear(within(form).getByLabelText('Distance (km)'))
    await user.type(within(form).getByLabelText('Distance (km)'), '7')
    await user.click(within(form).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(
        getFakeApi()
          .runsOf(token)
          .some((r) => r.date === '2026-11-05'),
      ).toBe(true),
    )
    const accept = requests.find((r) => r.path === `/api/training-suggestions/${suggestion.id}/accept`)
    expect(accept?.body).toMatchObject({ run: { date: '2026-11-05', distanceMeters: 7000, status: 'Planned' } })
    expect(
      getFakeApi()
        .suggestionsOf(token)
        .find((s) => s.id === suggestion.id),
    ).toBeUndefined()
    const created = getFakeApi()
      .runsOf(token)
      .find((r) => r.date === '2026-11-05')!
    expect(created.goalId).toBeTruthy()
    await waitFor(() => expect(router.state.location.pathname).toBe(`/diary/2026/11/runs/${created.id}`))
    // The run details link back to the goal.
    expect(await screen.findByRole('link', { name: 'From your plan for Autumn 10K' })).toHaveAttribute(
      'href',
      `/goals/${created.goalId}`,
    )
  })

  it('shows a gone state for an unknown suggestion', async () => {
    resetFakeApi({ goalsEnabled: true, guestSeed: HISTORY })
    signInForTest('guest')
    renderRoute('/diary/2026/11/suggestions/00000000-0000-4000-8000-00000000ffff/accept', { session: 'none' })
    expect(await screen.findByText('This suggestion no longer exists.')).toBeInTheDocument()
  })
})
