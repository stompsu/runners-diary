import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'

// Runs for the default fake (today 2026-11-03, guest seed = demoRuns): Nov 2026 starts on a Sunday, so the grid is
// Mon 26 Oct … Sun 6 Dec (6 ISO weeks, W44-W49). Week 45 (2-8 Nov) holds easy 8 km, tempo 10 km, long 21 km,
// recovery 5 km (completed) = 44 km, 4:10:00, 290 m; week 47 (16-22 Nov) holds planned easy 8 km + intervals 9 km.

async function renderMonth(path = '/diary/2026/11', options?: Parameters<typeof renderRoute>[1]) {
  const result = renderRoute(path, options)
  // `hidden`: a modal panel (deep link) marks the page behind it aria-hidden.
  const grid = await screen.findByRole('grid', { hidden: true })
  return { ...result, grid }
}

const cell = (date: string) => {
  const el = document.querySelector<HTMLElement>(`[data-date="${date}"]`)
  if (!el) throw new Error(`No cell for ${date}`)
  return el
}

describe('DiaryMonthPage – structure', () => {
  it('renders a labelled ARIA grid with a Monday-first header and one row per ISO week', async () => {
    const { grid } = await renderMonth()

    expect(grid).toHaveAccessibleName('Calendar for November 2026')
    const headers = within(grid).getAllByRole('columnheader')
    expect(headers.map((h) => h.textContent)).toEqual([
      'Week',
      'MonMonday',
      'TueTuesday',
      'WedWednesday',
      'ThuThursday',
      'FriFriday',
      'SatSaturday',
      'SunSunday',
    ])
    // 1 header row + 6 week rows
    expect(within(grid).getAllByRole('row')).toHaveLength(7)
    expect(within(grid).getAllByRole('rowheader')).toHaveLength(6)
    expect(within(grid).getAllByRole('gridcell')).toHaveLength(42)
    expect(cell('2026-10-26')).toBeInTheDocument()
    expect(cell('2026-12-06')).toBeInTheDocument()
  })

  it('shows 5 weeks for a month that needs only 5, and 4 for a February starting on a Monday', async () => {
    const { grid, unmount } = await renderMonth('/diary/2026/10')
    expect(within(grid).getAllByRole('rowheader')).toHaveLength(5)
    unmount()

    const feb = await renderMonth('/diary/2027/02') // 1 Feb 2027 is a Monday, 28 days
    expect(within(feb.grid).getAllByRole('rowheader')).toHaveLength(4)
  })

  it('marks days outside the month as dimmed but keeps them navigable', async () => {
    await renderMonth()

    expect(cell('2026-10-26')).toHaveAttribute('data-in-month', 'false')
    expect(cell('2026-12-06')).toHaveAttribute('data-in-month', 'false')
    expect(cell('2026-11-14')).toHaveAttribute('data-in-month', 'true')
    expect(cell('2026-10-26')).toHaveAttribute('tabindex', '-1')
  })

  it('names each cell with the full date and a summary of its runs', async () => {
    await renderMonth()

    expect(cell('2026-11-14')).toHaveAccessibleName('Saturday, November 14, 2026, 1 run: Road race, Completed, 21.1 km')
    expect(cell('2026-11-05')).toHaveAccessibleName('Thursday, November 5, 2026, no runs')
    expect(cell('2026-11-18')).toHaveAccessibleName('Wednesday, November 18, 2026, 1 run: Easy run, Planned, 8 km')
  })
})

describe('DiaryMonthPage – week totals', () => {
  it('shows completed distance, time and elevation per ISO week, and planned distance', async () => {
    await renderMonth()

    const week45 = screen.getByRole('rowheader', { name: /Week 45/ })
    expect(week45).toHaveTextContent('44 km')
    expect(week45).toHaveTextContent('4:10:00')
    expect(week45).toHaveTextContent('290 m')
    expect(week45).not.toHaveTextContent('Planned')

    const week47 = screen.getByRole('rowheader', { name: /Week 47/ })
    expect(week47).toHaveTextContent('0 km')
    expect(week47).toHaveTextContent('Planned 17 km')
  })

  it('keeps time and elevation in the accessibility tree on a phone (visible: distance only)', async () => {
    await renderMonth()

    const week45 = screen.getByRole('rowheader', { name: /Week 45/ })
    const time = within(week45).getByText('4:10:00')
    expect(time.closest('span')).toHaveClass('max-sm:sr-only')
    expect(within(week45).getByText('44 km').closest('span')).not.toHaveClass('max-sm:sr-only')
  })
})

describe('DiaryMonthPage – run bubbles', () => {
  it('renders completed runs filled and planned runs dashed, per type family', async () => {
    await renderMonth()

    const long = cell('2026-11-06').querySelector('button[data-status]')
    expect(long).toHaveAttribute('data-status', 'Completed')
    expect(long).toHaveAttribute('data-family', 'long')
    expect(long).toHaveClass('border-solid', 'bg-(--run)')
    expect(long).toHaveTextContent('Long run')
    expect(long).toHaveTextContent('21 km')

    const planned = cell('2026-11-21').querySelector('button[data-status]')
    expect(planned).toHaveAttribute('data-status', 'Planned')
    expect(planned).toHaveAttribute('data-family', 'quality')
    expect(planned).toHaveClass('border-dashed', 'bg-transparent')
  })

  it('marks races with a trophy and uses distinct icons for other families', async () => {
    await renderMonth()

    const race = cell('2026-11-14').querySelector('button[data-status]')
    expect(race).toHaveAttribute('data-family', 'race')
    expect(race?.querySelector('svg')).toHaveClass('lucide-trophy')
    const easy = cell('2026-11-02').querySelector('button[data-status]')
    expect(easy?.querySelector('svg')).not.toHaveClass('lucide-trophy')
  })

  it('renders the compact phone dots and day total next to the bubbles (shown via CSS < 640 px)', async () => {
    await renderMonth()

    const day = cell('2026-11-06')
    const dots = day.querySelector('.sm\\:hidden')
    expect(dots).toHaveTextContent('21')
    expect(dots?.querySelector('[data-status="Completed"]')).toBeInTheDocument()
    expect(day.querySelector('.sm\\:flex')).toBeInTheDocument()
    // Fixed table layout with a narrow week column: the grid cannot force horizontal scroll.
    expect(screen.getByRole('grid')).toHaveClass('table-fixed', 'w-full')
  })

  it('summarises days with more than three runs', async () => {
    const run = (n: number) => ({
      date: '2026-11-10',
      status: 'Completed' as const,
      runTypeId: 1,
      distanceMeters: 1000 * n,
    })
    resetFakeApi({ guestSeed: [run(1), run(2), run(3), run(4), run(5)] })
    await renderMonth()

    expect(cell('2026-11-10').querySelectorAll('button[data-status]')).toHaveLength(3)
    expect(within(cell('2026-11-10')).getByText('+2')).toBeInTheDocument()
    expect(cell('2026-11-10')).toHaveAccessibleName(/5 runs:/)
  })
})

describe('DiaryMonthPage – today comes from me.today', () => {
  it('highlights the API date, not the browser clock', async () => {
    resetFakeApi({ today: '2026-12-15', guestSeed: [] })
    await renderMonth('/diary/2026/12')

    expect(cell('2026-12-15')).toHaveAttribute('aria-current', 'date')
    expect(document.querySelectorAll('[aria-current="date"]')).toHaveLength(1)
    expect(cell('2026-12-15')).toHaveAttribute('tabindex', '0')
  })

  it('picks up a new me.today when the tab regains focus (stale /api/me only)', async () => {
    const { queryClient } = await renderMonth()
    expect(cell('2026-11-03')).toHaveAttribute('aria-current', 'date')

    getFakeApi().setToday('2026-11-04')
    queryClient
      .getQueryCache()
      .find({ queryKey: ['get', '/api/me'], exact: false })!
      .invalidate()
    window.dispatchEvent(new Event('visibilitychange'))

    await waitFor(() => expect(cell('2026-11-04')).toHaveAttribute('aria-current', 'date'))
    expect(document.querySelectorAll('[aria-current="date"]')).toHaveLength(1)
    // The default Add run date follows too.
    expect(screen.getByRole('link', { name: 'Add run' })).toHaveAttribute(
      'href',
      expect.stringContaining('date=2026-11-04'),
    )
  })

  it('does not refetch the calendar on focus (only /api/me does)', async () => {
    const { queryClient } = await renderMonth()
    const calendarCalls = () => getFakeApi().requests.filter((r) => r.startsWith('GET /api/calendar')).length
    const before = calendarCalls()

    queryClient.invalidateQueries({ queryKey: ['get', '/api/me'], refetchType: 'none' })
    window.dispatchEvent(new Event('visibilitychange'))
    await waitFor(() => expect(getFakeApi().requests.filter((r) => r === 'GET /api/me').length).toBeGreaterThan(1))

    expect(calendarCalls()).toBe(before)
  })

  it('does not highlight anything in a month that does not contain today', async () => {
    await renderMonth('/diary/2026/03')

    expect(document.querySelector('[aria-current="date"]')).toBeNull()
    // The roving tab stop falls back to the 1st of the month.
    expect(cell('2026-03-01')).toHaveAttribute('tabindex', '0')
  })

  it('/ and /diary redirect to the month of me.today', async () => {
    resetFakeApi({ today: '2027-02-10' })
    const { router } = renderRoute('/')

    await screen.findByRole('grid')
    expect(router.state.location.pathname).toBe('/diary/2027/02')
    expect(cell('2027-02-10')).toHaveAttribute('aria-current', 'date')
  })

  it('the Today button goes to the month of me.today and focuses today', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth('/diary/2026/03')

    await user.click(screen.getByRole('button', { name: 'Today' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11'))
    await waitFor(() => expect(cell('2026-11-03')).toHaveFocus())
  })

  it('the Today button focuses today when already on its month', async () => {
    const user = userEvent.setup()
    await renderMonth()
    cell('2026-11-20').focus()

    await user.click(screen.getByRole('button', { name: 'Today' }))

    await waitFor(() => expect(cell('2026-11-03')).toHaveFocus())
  })
})

describe('DiaryMonthPage – month announcement', () => {
  it('announces nothing on first render, then the new month after prev/next, Today and the picker', async () => {
    const user = userEvent.setup()
    await renderMonth('/diary/2026/03')
    const status = screen.getByTestId('month-announcement')
    expect(status).toBeEmptyDOMElement()

    await user.click(screen.getByRole('link', { name: 'Next month' }))
    await waitFor(() => expect(status).toHaveTextContent('Showing April 2026'))

    await user.click(screen.getByRole('link', { name: 'Previous month' }))
    await waitFor(() => expect(status).toHaveTextContent('Showing March 2026'))

    await user.click(screen.getByRole('button', { name: 'Today' }))
    await waitFor(() => expect(status).toHaveTextContent('Showing November 2026'))
  })
})

describe('DiaryMonthPage – day panel URL', () => {
  it.each(['2026-02-30', '2026-13-01', 'abc', '2026-11-3'])(
    'ignores the invalid day/%s as the selected day and keeps today as the tab stop',
    async (value) => {
      await renderMonth(`/diary/2026/11/day/${value}`)

      expect(document.querySelector('[data-selected]')).toBeNull()
      expect(cell('2026-11-03')).toHaveAttribute('tabindex', '0')
    },
  )

  it('marks a valid day/:date as selected', async () => {
    await renderMonth('/diary/2026/11/day/2026-11-10')

    expect(cell('2026-11-10')).toHaveAttribute('data-selected', 'true')
  })
})

describe('DiaryMonthPage – keyboard', () => {
  it('has exactly one tab stop: today when it is in view', async () => {
    await renderMonth()

    const stops = screen.getAllByRole('gridcell').filter((c) => c.getAttribute('tabindex') === '0')
    expect(stops).toEqual([cell('2026-11-03')])
  })

  it('tabs into the grid on today, and the roving stop follows the focus', async () => {
    const user = userEvent.setup()
    await renderMonth()

    cell('2026-11-03').focus()
    await user.keyboard('{ArrowRight}{ArrowDown}')

    expect(cell('2026-11-11')).toHaveFocus()
    expect(cell('2026-11-11')).toHaveAttribute('tabindex', '0')
    expect(cell('2026-11-03')).toHaveAttribute('tabindex', '-1')
  })

  it.each([
    ['{ArrowLeft}', '2026-11-13'],
    ['{ArrowRight}', '2026-11-15'],
    ['{ArrowUp}', '2026-11-07'],
    ['{ArrowDown}', '2026-11-21'],
    ['{Home}', '2026-11-09'],
    ['{End}', '2026-11-15'],
  ])('%s from Sat 14 Nov moves the focus to %s', async (keys, expected) => {
    const user = userEvent.setup()
    await renderMonth()
    cell('2026-11-14').focus()

    await user.keyboard(keys)

    expect(cell(expected)).toHaveFocus()
  })

  it('moves into dimmed days of the neighbouring months without changing the URL', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()
    cell('2026-11-02').focus()

    await user.keyboard('{ArrowUp}')

    expect(cell('2026-10-26')).toHaveFocus()
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('crossing the grid edge changes the month and keeps the focus on the same date', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()
    cell('2026-12-06').focus()

    await user.keyboard('{ArrowRight}')

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/12'))
    await waitFor(() => expect(cell('2026-12-07')).toHaveFocus())
  })

  it('PageDown / PageUp change the month and keep the day number (clamped)', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()
    cell('2026-11-30').focus()

    await user.keyboard('{PageDown}')
    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/12'))
    await waitFor(() => expect(cell('2026-12-30')).toHaveFocus())

    cell('2026-12-31').focus()
    await user.keyboard('{PageUp}')
    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11'))
    await waitFor(() => expect(cell('2026-11-30')).toHaveFocus())
  })

  it('does not move past the supported range', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth('/diary/1950/01')
    cell('1950-01-10').focus()

    await user.keyboard('{PageUp}')

    expect(router.state.location.pathname).toBe('/diary/1950/01')
    expect(cell('1950-01-10')).toHaveFocus()
  })

  it.each(['{Enter}', ' '])('Enter and Space open the day (%j)', async (key) => {
    const user = userEvent.setup()
    const { router } = await renderMonth()
    cell('2026-11-14').focus()

    await user.keyboard(key)

    expect(router.state.location.pathname).toBe('/diary/2026/11/day/2026-11-14')
    expect(await screen.findByRole('dialog', { name: 'Saturday, November 14, 2026' })).toBeInTheDocument()
  })

  it('opens a day by click and a run by clicking its bubble', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()

    await user.click(cell('2026-11-05'))
    // Panel routes are lazy: the URL changes once the chunk has loaded.
    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11/day/2026-11-05'))
    await screen.findByRole('dialog')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    const bubble = cell('2026-11-06').querySelector<HTMLElement>('button[data-run-id]')!
    await user.click(bubble)
    await waitFor(() => expect(router.state.location.pathname).toBe(`/diary/2026/11/runs/${bubble.dataset.runId}`))
  })

  it('a deep link to a day marks it selected and makes it the tab stop', async () => {
    await renderMonth('/diary/2026/11/day/2026-11-14')

    expect(cell('2026-11-14')).toHaveAttribute('data-selected', 'true')
    expect(cell('2026-11-14')).toHaveAttribute('tabindex', '0')
    expect(cell('2026-11-03')).toHaveAttribute('tabindex', '-1')
    expect(await screen.findByRole('dialog', { name: 'Saturday, November 14, 2026' })).toBeInTheDocument()
  })
})

describe('DiaryMonthPage – navigation and URL', () => {
  it('prev/next change the month in the URL and Back/Forward restore it', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()

    await user.click(screen.getByRole('link', { name: 'Next month' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – December 2026' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/diary/2026/12')

    await user.click(screen.getByRole('link', { name: 'Previous month' }))
    await user.click(screen.getByRole('link', { name: 'Previous month' }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – October 2026' })).toBeInTheDocument()

    await router.navigate(-1)
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeInTheDocument()
    await router.navigate(-1)
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – December 2026' })).toBeInTheDocument()
    await router.navigate(1)
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('wraps over year boundaries', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth('/diary/2026/12')

    await user.click(screen.getByRole('link', { name: 'Next month' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2027/01'))
  })

  it('disables prev at 1950-01 and next at 2100-12', async () => {
    const first = await renderMonth('/diary/1950/01')
    expect(screen.getByRole('button', { name: 'Previous month' })).toBeDisabled()
    expect(screen.getByRole('link', { name: 'Next month' })).toBeInTheDocument()
    first.unmount()

    await renderMonth('/diary/2100/12')
    expect(screen.getByRole('button', { name: 'Next month' })).toBeDisabled()
  })

  it('jumps to any month through the month/year picker', async () => {
    const user = userEvent.setup()
    const { router } = await renderMonth()

    await user.click(screen.getByRole('button', { name: 'November 2026, choose month and year' }))
    await user.click(screen.getByRole('button', { name: 'Next year' }))
    expect(screen.getByText('Year 2027')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'March 2027' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2027/03'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – March 2027' })).toBeInTheDocument()
  })

  it.each(['/diary/2026/13', '/diary/2026/0', '/diary/1949/12', '/diary/2101/01', '/diary/abc/11'])(
    'renders Not found for %s without calling the calendar API',
    async (path) => {
      renderRoute(path)

      expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
      expect(getFakeApi().requests.filter((r) => r.includes('/api/calendar'))).toEqual([])
    },
  )

  it('links to the month list and to Add run with a prefilled date', async () => {
    await renderMonth()

    expect(screen.getByRole('link', { name: 'Month list' })).toHaveAttribute('href', '/diary/2026/11/list')
    expect(screen.getByRole('link', { name: 'Add run' })).toHaveAttribute(
      'href',
      '/diary/2026/11/runs/new?date=2026-11-03',
    )
  })

  it('prefills Add run with the 1st when today is in another month', async () => {
    await renderMonth('/diary/2026/03')

    expect(screen.getByRole('link', { name: 'Add run' })).toHaveAttribute(
      'href',
      '/diary/2026/03/runs/new?date=2026-03-01',
    )
  })

  it('prefetches the neighbouring months once the visible one has loaded', async () => {
    await renderMonth()

    await waitFor(() => {
      const requests = getFakeApi().requests
      expect(requests).toContain('GET /api/calendar/months/2026/10')
      expect(requests).toContain('GET /api/calendar/months/2026/12')
    })
  })
})

describe('DiaryMonthPage – loading and errors', () => {
  it('shows an error state with Try again, and recovers', async () => {
    getFakeApi().addFault(faults.status({ path: '/api/calendar/months/2026/11' }, 500, 1))
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong. Please try again.')

    await user.click(within(alert).getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('grid')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('explains an unreachable server', async () => {
    getFakeApi().addFault(faults.html({ path: '/api/calendar/months/2026/11' }, 502, 4))
    renderRoute('/diary/2026/11')

    expect(
      await screen.findByText('The server is not reachable right now. Please try again.', {}, { timeout: 20_000 }),
    ).toBeInTheDocument()
  }, 25_000)

  it('keeps the header usable while the calendar is loading, and shows the slow-start notice after 3 s', async () => {
    getFakeApi().addFault(faults.delay({ path: '/api/calendar/months/2026/11' }, 3300, 1))
    renderRoute('/diary/2026/11')

    expect(await screen.findByRole('heading', { level: 1, name: 'Diary – November 2026' })).toBeInTheDocument()
    expect(screen.queryByRole('grid')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Next month' })).toBeInTheDocument()

    expect(await screen.findByText(/Waking up the server/, {}, { timeout: 4000 })).toBeInTheDocument()
    expect(await screen.findByRole('grid', {}, { timeout: 4000 })).toBeInTheDocument()
  }, 12_000)

  it('shows a message when me cannot be loaded', async () => {
    getFakeApi().addFault(faults.status({ path: '/api/me' }, 500, 1))
    renderRoute('/')

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong')
  })
})
