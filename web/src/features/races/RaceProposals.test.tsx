import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { DayCell } from '@/features/calendar/DayCell'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { DEMO_RACE_IDS } from '@/test/fakeApi'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute, signInForTest } from '@/test/render'
import { recordRequests, respondOnce } from '@/test/requests'
import { stubResizeObserver } from '@/test/resizeObserver'
import { setPhoneViewport } from '@/test/viewport'
import '@/i18n/lazyBundles'

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeEach(() => {
  stubResizeObserver()
  resetFakeMap()
  setPhoneViewport(false)
  vi.mocked(supportsWebGl2).mockReturnValue(true)
})

// Default fake: today 2026-11-03, guest with the demo profile. The November grid is Mon 26 Oct ... Sun 6 Dec.
// Demo catalogue (today + n): Gliwice Night 10K Nov 12, Silesian Trail Weekend (10 / 21.1 / 42.2 km) Nov 21,
// Katowice Half Nov 29, Tychy 10K (removed, Interested) Dec 3, Rybnik 5K (date changed, Interested) Dec 11.
const NIGHT = DEMO_RACE_IDS.night10k
const TRAIL = DEMO_RACE_IDS.silesianTrail

const cell = (date: string) => {
  const el = document.querySelector<HTMLElement>(`[data-date="${date}"]`)
  if (!el) throw new Error(`No cell for ${date}`)
  return el
}
const bubbles = () => document.querySelectorAll('button[data-kind="proposal"]')

async function renderMonth(path = '/diary/2026/11', options: Parameters<typeof resetFakeApi>[0] = {}) {
  resetFakeApi(options)
  const token = signInForTest('guest')
  const result = renderRoute(path, { session: 'none' })
  await screen.findByRole('grid', { hidden: true })
  return { token, ...result }
}

/** Calls the fake API directly with a token (state set-up that is not what the test is about). */
async function api(token: string, method: string, path: string, body?: unknown) {
  const response = await getFakeApi().handle(
    new Request(`http://localhost${path}`, {
      method,
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }),
  )
  return response
}

const run = (over: Record<string, unknown>) =>
  ({ id: 'r', status: 'Planned', runTypeId: 20, distanceMeters: 8000, ...over }) as never
const proposal = (id: string, state = 'None') =>
  ({
    raceId: id,
    name: `Race ${id}`,
    date: '2026-11-12',
    distancesMeters: [10000, 21097.5],
    state,
  }) as never

describe('Grid: proposal bubbles', () => {
  it('draws a dotted bubble with name and distances, a star for Interested, and no bubble for signed-up races', async () => {
    const { token } = await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))

    const night = cell('2026-11-12').querySelector<HTMLElement>('button[data-kind="proposal"]')!
    expect(night).toHaveAttribute('aria-hidden', 'true')
    expect(night).toHaveAttribute('data-state', 'None')
    expect(night).toHaveTextContent('Demo Gliwice Night 10K')
    expect(night).toHaveTextContent('10 km')
    expect(night.className).toContain('border-dotted')
    expect(cell('2026-11-21').querySelector('button[data-kind="proposal"]')).toHaveTextContent('10 / 21.1 / 42.2 km')
    // Interested (and removed): the star shows and the state is on the element.
    expect(cell('2026-12-03').querySelector('button[data-kind="proposal"]')).toHaveAttribute('data-state', 'Interested')
    // The out-of-region race is filtered by the profile.
    expect(document.querySelector(`[data-race-id="${DEMO_RACE_IDS.warsaw10k}"]`)).toBeNull()
    expect(token).toBeTruthy()
  })

  it('hides the bubble of a race the user signed up to (the run shows instead)', async () => {
    resetFakeApi()
    const token = signInForTest('guest')
    await api(token, 'POST', `/api/races/${NIGHT}/signup`, {})
    renderRoute('/diary/2026/11', { session: 'none' })
    await screen.findByRole('grid', { hidden: true })

    await waitFor(() => expect(cell('2026-11-21').querySelector('button[data-kind="proposal"]')).not.toBeNull())
    expect(cell('2026-11-12').querySelector('button[data-kind="proposal"]')).toBeNull()
    expect(cell('2026-11-12').querySelector('button[data-run-id]')).not.toBeNull()
  })

  it('renders a hollow flag dot for the phone layout next to the bubble', async () => {
    await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    expect(cell('2026-11-12').querySelector('svg[data-kind="proposal"]')).not.toBeNull()
  })

  it('opens the race from a bubble click', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    await user.click(cell('2026-11-12').querySelector('button[data-kind="proposal"]')!)
    expect(router.state.location.pathname).toBe(`/diary/2026/11/races/${NIGHT}`)
  })

  it('appends the proposals to the cell name', async () => {
    await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    expect(cell('2026-11-12')).toHaveAccessibleName(
      'Thursday, November 12, 2026, no runs; 1 race proposal: Demo Gliwice Night 10K, 10 km',
    )
    expect(cell('2026-11-21')).toHaveAccessibleName(
      'Saturday, November 21, 2026, 1 run: Intervals, Planned, 9 km; 1 race proposal: Demo Silesian Trail Weekend, 10 / 21.1 / 42.2 km',
    )
    expect(cell('2026-12-03')).toHaveAccessibleName(/1 race proposal: Demo Tychy 10K, 10 km, interested$/)
    expect(cell('2026-11-14')).toHaveAccessibleName('Saturday, November 14, 2026, 1 run: Road race, Completed, 21.1 km')
  })

  it('does not ask for proposals without a profile or with the layer off', async () => {
    await renderMonth('/diary/2026/11', { guestRaceProfile: false })
    await screen.findByRole('link', { name: 'Set up race proposals' })
    expect(getFakeApi().requests).not.toContain('GET /api/races/proposals')
    expect(bubbles()).toHaveLength(0)
  })
})

describe('DayCell: order and cap', () => {
  it('shows runs first, then proposals, sharing one cap and one "+n"', () => {
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
              runs={[run({ id: 'a' }), run({ id: 'b' })]}
              proposals={[proposal('p1'), proposal('p2')]}
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
    const order = Array.from(document.querySelectorAll('button[data-run-id], button[data-kind="proposal"]')).map(
      (el) => (el.getAttribute('data-kind') === 'proposal' ? 'proposal' : 'run'),
    )
    expect(order).toEqual(['run', 'run', 'proposal'])
    expect(screen.getByText('+1')).toBeInTheDocument()
  })
})

describe('Legend and toggle', () => {
  it('lists completed, planned, race and (while shown) race proposal', async () => {
    await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    const legend = screen.getByRole('list', { name: 'Legend', hidden: true })
    for (const name of ['Completed', 'Planned', 'Race', 'Race proposal']) {
      expect(within(legend).getByText(name)).toBeInTheDocument()
    }
  })

  it('updates optimistically, saves showInCalendar and drops the proposal entry', async () => {
    const user = userEvent.setup()
    const { token } = await renderMonth()
    const requests = recordRequests()
    const toggle = await screen.findByRole('button', { name: 'Show race proposals', hidden: true })
    expect(toggle).toHaveAttribute('aria-pressed', 'true')
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))

    await user.click(toggle)

    await waitFor(() => expect(toggle).toHaveAttribute('aria-pressed', 'false'))
    expect(bubbles()).toHaveLength(0)
    expect(within(screen.getByRole('list', { name: 'Legend', hidden: true })).queryByText('Race proposal')).toBeNull()
    await waitFor(() => expect(getFakeApi().raceInterestsOf(token)?.showInCalendar).toBe(false))
    const put = requests.find((r) => r.method === 'PUT' && r.path === '/api/races/interests')
    expect(put?.body).toMatchObject({ showInCalendar: false, regionCodes: ['PL-24'], horizonDays: 120 })
  })

  it('rolls back when the save fails', async () => {
    const user = userEvent.setup()
    await renderMonth()
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
    respondOnce('PUT', '/api/races/interests', 500, {})
    const toggle = await screen.findByRole('button', { name: 'Show race proposals', hidden: true })

    await user.click(toggle)

    await waitFor(() => expect(toggle).toHaveAttribute('aria-pressed', 'true'))
    await waitFor(() => expect(bubbles().length).toBeGreaterThan(0))
  })

  it('is a link to the settings without a profile and absent while the feature is off', async () => {
    await renderMonth('/diary/2026/11', { guestRaceProfile: false })
    expect(await screen.findByRole('link', { name: 'Set up race proposals' })).toHaveAttribute(
      'href',
      '/settings/races',
    )
  })

  it('renders neither toggle, list button nor legend entry when races are disabled', async () => {
    await renderMonth('/diary/2026/11', { racesEnabled: false })
    expect(screen.queryByRole('button', { name: 'Show race proposals' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Race proposals' })).toBeNull()
    expect(screen.queryByText('Race proposal')).toBeNull()
  })
})

describe('Day panel', () => {
  it('has a Race proposals section with real links and buttons', async () => {
    resetFakeApi()
    signInForTest('guest')
    renderRoute('/diary/2026/11/day/2026-11-12', { session: 'none' })
    const dialog = await screen.findByRole('dialog')
    const section = await within(dialog).findByRole('region', { name: 'Race proposals' })
    expect(within(section).getByRole('link', { name: 'Details' })).toHaveAttribute(
      'href',
      `/diary/2026/11/races/${NIGHT}`,
    )
    expect(within(section).getByRole('button', { name: 'Interested' })).toHaveAttribute('aria-pressed', 'false')
  })
})

describe('Proposals list panel', () => {
  async function openList(options: Parameters<typeof resetFakeApi>[0] = {}) {
    resetFakeApi(options)
    const token = signInForTest('guest')
    const user = userEvent.setup()
    const result = renderRoute('/diary/2026/11/races', { session: 'none' })
    const dialog = await screen.findByRole('dialog', { name: 'Race proposals – November 2026' })
    await within(dialog).findByText('Demo Gliwice Night 10K')
    return { token, user, dialog, ...result }
  }

  it('groups the rows by date, badges the new ones and marks the list as seen', async () => {
    const { token, dialog } = await openList()

    const headings = within(dialog).getAllByRole('heading', { level: 3 })
    expect(headings.map((h) => h.textContent).filter((text) => /2026/.test(text ?? ''))).toEqual([
      'Thursday, November 12, 2026',
      'Saturday, November 21, 2026',
      'Sunday, November 29, 2026',
    ])
    // The three unseen proposals of the month are New.
    expect(within(dialog).getAllByText('New')).toHaveLength(3)
    await waitFor(() => expect(getFakeApi().raceSeenOf(token)).not.toBeNull())
    expect(getFakeApi().requests).toContain('POST /api/races/proposals/seen')
    // The badges stay while the panel is open.
    expect(within(dialog).getAllByText('New')).toHaveLength(3)
  })

  it('says the window is clamped to today and when the race list was updated', async () => {
    const { dialog } = await openList({ catalogueUpdatedUtc: '2026-10-31T23:30:00Z' })
    expect(within(dialog).getByText('Proposals cover Nov 3 – 30, 2026.')).toBeInTheDocument()
    // 23:30 UTC is already 1 November in the user's zone (Europe/Warsaw).
    expect(within(dialog).getByText('Race list updated Nov 1, 2026')).toBeInTheDocument()
    expect(within(dialog).queryByRole('note')).toBeNull()
  })

  it('warns when the race list is more than 3 weeks old', async () => {
    const { dialog } = await openList({ catalogueUpdatedUtc: '2026-10-05T10:00:00Z' })
    expect(within(dialog).getByRole('note')).toHaveTextContent(/not been refreshed for more than 3 weeks/)
  })

  it('marks Interested and hides a dismissed race until "Show dismissed" is on', async () => {
    const { user, dialog, token } = await openList()
    const night = () => dialog.querySelector<HTMLElement>(`li[data-race-id="${NIGHT}"]`)

    await user.click(within(night()!).getByRole('button', { name: 'Interested' }))
    await waitFor(() => expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('Interested'))
    await waitFor(() =>
      expect(within(night()!).getByRole('button', { name: 'Interested' })).toHaveAttribute('aria-pressed', 'true'),
    )

    await user.click(within(night()!).getByRole('button', { name: 'Not interested' }))
    await waitFor(() => expect(night()).toBeNull())
    expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('Dismissed')

    await user.click(within(dialog).getByRole('switch', { name: 'Show dismissed' }))
    await waitFor(() => expect(night()).not.toBeNull())
    await user.click(within(night()!).getByRole('button', { name: 'Undo' }))
    await waitFor(() => expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('None'))
  })

  it('offers the settings link without a profile', async () => {
    resetFakeApi({ guestRaceProfile: false })
    signInForTest('guest')
    renderRoute('/diary/2026/11/races', { session: 'none' })
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByRole('link', { name: 'Set up race proposals' })).toBeInTheDocument()
  })
})

describe('Race details dialog', () => {
  async function openRace(
    id: string,
    options: Parameters<typeof resetFakeApi>[0] = {},
    prepare?: (token: string) => Promise<void>,
  ) {
    resetFakeApi(options)
    const token = signInForTest('guest')
    await prepare?.(token)
    const user = userEvent.setup()
    const result = renderRoute(`/diary/2026/11/races/${id}`, { session: 'none' })
    const dialog = await screen.findByRole('dialog')
    await within(dialog)
      .findByRole('heading', { name: /Demo|Race not found/ })
      .catch(() => undefined)
    return { token, user, dialog, ...result }
  }

  it('shows the facts, the attribution and the external links with safe attributes', async () => {
    const { dialog } = await openRace(NIGHT)
    await within(dialog).findByText('Gliwice, Silesian')

    expect(dialog).toHaveAccessibleName('Demo Gliwice Night 10K')
    expect(within(dialog).getByText('Thursday, November 12, 2026 · 19:30')).toBeInTheDocument()
    expect(within(dialog).getByText('PLN 60')).toBeInTheDocument()
    expect(within(dialog).getByText('Sunday, November 8, 2026 (closes in 5 days)')).toBeInTheDocument()
    const source = within(dialog).getByRole('link', { name: /Source: running\.life/ })
    expect(source).toHaveAttribute('href', 'https://running.life/events/demo-gliwice-night-10k')
    const signup = within(dialog).getByRole('link', { name: /Open signup page/ })
    expect(signup).toHaveAttribute('href', 'https://example.org/gliwice-night/signup')
    for (const link of [source, signup, within(dialog).getByRole('link', { name: /Event website/ })]) {
      expect(link).toHaveAttribute('target', '_blank')
      expect(link).toHaveAttribute('rel', 'noopener noreferrer')
      expect(link).toHaveAttribute('referrerpolicy', 'no-referrer')
    }
  })

  it('renders a flag pin on the map with the coordinates, or says there is no position', async () => {
    const { dialog } = await openRace(NIGHT)
    const map = await within(dialog).findByTestId('fake-map')
    expect(map).toHaveAttribute('data-marker-kind', 'race')
    expect(map).toHaveAttribute('data-marker', '50.2945,18.6714')
    expect(within(dialog).getByText('Coordinates: 50.29450, 18.67140')).toBeInTheDocument()
  })

  it('says there is no map position when the source gave none', async () => {
    const { dialog } = await openRace(DEMO_RACE_IDS.tychy10k)
    expect(await within(dialog).findByText('No map position from the source')).toBeInTheDocument()
  })

  it('does not render a javascript: URL as a link and disables the signup page with a reason', async () => {
    resetFakeApi()
    getFakeApi().patchRace(NIGHT, {
      signupUrl: 'javascript:alert(1)',
      eventUrl: 'data:text/html,x',
      sourceUrl: 'javascript:alert(2)',
    })
    signInForTest('guest')
    renderRoute(`/diary/2026/11/races/${NIGHT}`, { session: 'none' })
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('Gliwice, Silesian')

    expect(within(dialog).queryByRole('link', { name: /Open signup page/ })).toBeNull()
    expect(within(dialog).queryByRole('link', { name: /Event website/ })).toBeNull()
    const button = within(dialog).getByRole('button', { name: 'Open signup page' })
    expect(button).toBeDisabled()
    expect(button).toHaveAccessibleDescription('The source did not provide a signup link.')
    // The attribution falls back to the source's own home page from the client config.
    expect(within(dialog).getByRole('link', { name: /Source: running\.life/ })).toHaveAttribute(
      'href',
      'https://running.life/',
    )
    expect(document.querySelector('a[href^="javascript:"]')).toBeNull()
  })

  it('says the race is gone for an unknown id', async () => {
    await openRace('99999999-9999-4999-8999-999999999999')
    expect(await screen.findByRole('dialog', { name: 'Race not found' })).toBeInTheDocument()
  })

  it('signs up to a single-distance race with the optional time and opens the new run', async () => {
    const { user, dialog, token, router } = await openRace(NIGHT)
    const requests = recordRequests()
    await user.click(await within(dialog).findByRole('button', { name: 'I signed up' }))
    const form = within(dialog).getByRole('form', { name: 'I signed up' })
    expect(within(form).queryByRole('radio')).toBeNull()
    expect(within(form).getByText('Distance: 10 km')).toBeInTheDocument()
    await user.type(within(form).getByLabelText('Target time (optional, h:mm:ss)'), '0:55:00')
    await user.click(within(form).getByRole('button', { name: 'Add to my diary' }))

    await waitFor(() => expect(router.state.location.pathname).toMatch(/\/runs\/[0-9a-f-]+$/))
    const post = requests.find((r) => r.method === 'POST' && r.path === `/api/races/${NIGHT}/signup`)
    expect(post?.body).toEqual({ distanceMeters: 10000, startTime: '19:30', targetTimeSeconds: 3300 })
    const created = getFakeApi()
      .runsOf(token)
      .find((r) => r.catalogRaceId === NIGHT)
    expect(created).toMatchObject({ status: 'Planned', date: '2026-11-12', race: { distanceCategory: 'TenK' } })
    // The run write refreshed the calendar (shared run-write invalidation).
    await waitFor(() => expect(requests.filter((r) => r.path.startsWith('/api/calendar/')).length).toBeGreaterThan(0))
  })

  it('requires a distance when the race has several and sends the chosen one', async () => {
    const { user, dialog, router } = await openRace(TRAIL)
    const requests = recordRequests()
    await user.click(await within(dialog).findByRole('button', { name: 'I signed up' }))
    const form = within(dialog).getByRole('form', { name: 'I signed up' })
    expect(within(form).getAllByRole('radio')).toHaveLength(3)

    await user.click(within(form).getByRole('button', { name: 'Add to my diary' }))
    expect(await within(form).findByText('Choose a distance.')).toBeInTheDocument()
    expect(requests.some((r) => r.method === 'POST')).toBe(false)

    await user.click(within(form).getByRole('radio', { name: '21.1 km' }))
    await user.click(within(form).getByRole('button', { name: 'Add to my diary' }))
    await waitFor(() => expect(router.state.location.pathname).toMatch(/\/runs\//))
    expect(requests.find((r) => r.method === 'POST')?.body).toMatchObject({ distanceMeters: 21097.5 })
  })

  it('validates the optional start and target time before sending', async () => {
    const { user, dialog } = await openRace(NIGHT)
    await user.click(await within(dialog).findByRole('button', { name: 'I signed up' }))
    const form = within(dialog).getByRole('form', { name: 'I signed up' })
    await user.type(within(form).getByLabelText('Target time (optional, h:mm:ss)'), 'soon')
    await user.click(within(form).getByRole('button', { name: 'Add to my diary' }))
    expect(await within(form).findByText('Enter a time like 1:45:00.')).toBeInTheDocument()
  })

  it('handles 409 race-already-linked with a link to the existing run', async () => {
    const { user, dialog } = await openRace(NIGHT)
    await user.click(await within(dialog).findByRole('button', { name: 'I signed up' }))
    respondOnce('POST', `/api/races/${NIGHT}/signup`, 409, {
      type: 'urn:runnersdiary:problem:race-already-linked',
      runId: 'existing-run',
    })
    await user.click(within(dialog).getByRole('button', { name: 'Add to my diary' }))
    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('This race is already in your diary.')
    expect(within(alert).getByRole('link', { name: 'View my planned run' })).toHaveAttribute(
      'href',
      '/diary/2026/11/runs/existing-run',
    )
  })

  it('shows the planned run and offers to move it when the listing date changed', async () => {
    const { user, dialog, token } = await openRace(NIGHT, {}, async (t) => {
      await api(t, 'POST', `/api/races/${NIGHT}/signup`, {})
      getFakeApi().patchRace(NIGHT, { date: '2026-11-13' })
    })
    const banner = await within(dialog).findByText(
      'The race listing says Friday, November 13, 2026; your planned run is on Thursday, November 12, 2026.',
    )
    expect(within(dialog).getByRole('link', { name: 'View my planned run' })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'I signed up' })).toBeNull()
    const requests = recordRequests()

    await user.click(within(dialog).getByRole('button', { name: 'Move my run to Friday, November 13, 2026' }))

    await waitFor(() => expect(banner).not.toBeInTheDocument())
    const put = requests.find((r) => r.method === 'PUT' && r.path.startsWith('/api/runs/'))
    // A full replace: the other fields of the run are sent as they were.
    expect(put?.body).toMatchObject({ date: '2026-11-13', status: 'Planned', race: { name: 'Demo Gliwice Night 10K' } })
    expect(
      getFakeApi()
        .runsOf(token)
        .find((r) => r.catalogRaceId === NIGHT)?.date,
    ).toBe('2026-11-13')
  })

  it('asks to reload when moving the run hits a concurrency conflict', async () => {
    const { user, dialog } = await openRace(NIGHT, {}, async (t) => {
      await api(t, 'POST', `/api/races/${NIGHT}/signup`, {})
      getFakeApi().patchRace(NIGHT, { date: '2026-11-13' })
    })
    await within(dialog).findByText(/The race listing says/)
    respondOnce('PUT', /^\/api\/runs\//, 409, { type: 'urn:runnersdiary:problem:concurrency-conflict' })
    await user.click(within(dialog).getByRole('button', { name: /Move my run to/ }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('changed elsewhere')
    expect(within(dialog).getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('lets an Interested user acknowledge a changed date with OK', async () => {
    const { user, dialog } = await openRace(DEMO_RACE_IDS.rybnik5k)
    const requests = recordRequests()
    expect(await within(dialog).findByText(/The listing now says/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'OK' }))
    await waitFor(() => expect(within(dialog).queryByText(/The listing now says/)).toBeNull())
    expect(requests.find((r) => r.method === 'PUT' && r.path.endsWith('/state'))?.body).toEqual({ state: 'Interested' })
  })

  it('warns that a removed race is no longer listed and disables signing up', async () => {
    const { dialog } = await openRace(DEMO_RACE_IDS.tychy10k)
    expect(await within(dialog).findByText(/No longer listed by the source/)).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'I signed up' })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Open signup page' })).toBeDisabled()
  })

  it('links a duplicate to the main listing', async () => {
    const { dialog } = await openRace(DEMO_RACE_IDS.night10kDuplicate)
    expect(await within(dialog).findByRole('link', { name: 'Open the main listing' })).toHaveAttribute(
      'href',
      `/diary/2026/11/races/${NIGHT}`,
    )
  })

  it('toggles Interested and Not interested / Undo', async () => {
    const { user, dialog, token } = await openRace(NIGHT)
    await user.click(await within(dialog).findByRole('button', { name: 'Interested' }))
    await waitFor(() => expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('Interested'))
    await user.click(await within(dialog).findByRole('button', { name: 'Not interested' }))
    await waitFor(() => expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('Dismissed'))
    await user.click(await within(dialog).findByRole('button', { name: 'Undo' }))
    await waitFor(() => expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('None'))
  })
})

describe('Run details: link to the listing', () => {
  it('links a signed-up run to its race', async () => {
    resetFakeApi()
    const token = signInForTest('guest')
    const response = await api(token, 'POST', `/api/races/${NIGHT}/signup`, {})
    const created = (await response.json()) as { id: string }
    renderRoute(`/diary/2026/11/runs/${created.id}`, { session: 'none' })
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByRole('link', { name: 'From race listing' })).toHaveAttribute(
      'href',
      `/diary/2026/11/races/${NIGHT}`,
    )
  })
})

describe('After a run is deleted the proposal comes back', () => {
  it('derives SignedUp from the run, so deleting it makes the race a proposal again', async () => {
    resetFakeApi()
    const token = signInForTest('guest')
    const created = (await (await api(token, 'POST', `/api/races/${NIGHT}/signup`, {})).json()) as { id: string }
    expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('SignedUp')
    await api(token, 'DELETE', `/api/runs/${created.id}`)
    expect(getFakeApi().raceStateOf(token, NIGHT)).toBe('None')
  })
})
