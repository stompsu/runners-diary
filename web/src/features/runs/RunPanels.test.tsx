import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, typeId, type RunWriteRequest } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'
import { deleteElsewhere, recordRequests, storedRuns } from '@/test/requests'
import { setPhoneViewport } from '@/test/viewport'

// Default fake: today 2026-11-03, guest seed = demoRuns. Runs of interest (Nov 2026):
// 02 easy 8 km (completed), 04 tempo 10 km (rated 4), 06 long 21 km (unrated), 11 rest day, 14 road race (rated 5),
// 18 easy 8 km planned, 21 intervals planned 18:00, 25 long planned.

const cell = (date: string) => document.querySelector<HTMLElement>(`[role="gridcell"][data-date="${date}"]`)!
const DAY_14 = 'Saturday, November 14, 2026'

/** Panels open before their data has arrived; wait for the skeleton to go. */
const loaded = (dialog: HTMLElement) =>
  waitFor(() => expect(dialog.querySelector('[data-slot="skeleton"]')).not.toBeInTheDocument())

async function openDay(date: string, title: string) {
  const result = renderRoute(`/diary/2026/11/day/${date}`)
  const dialog = await screen.findByRole('dialog', { name: title })
  await loaded(dialog)
  return { ...result, dialog }
}

const itemFor = (dialog: HTMLElement, text: RegExp | string) =>
  within(dialog)
    .getAllByRole('listitem')
    .find((item) => (typeof text === 'string' ? item.textContent?.includes(text) : text.test(item.textContent ?? '')))!

describe('Day panel', () => {
  it('lists the day with status, metrics and per-run actions', async () => {
    const { dialog } = await openDay('2026-11-14', DAY_14)

    const race = within(dialog).getByRole('listitem')
    expect(within(race).getByRole('heading', { level: 3 })).toHaveTextContent('Road race · Silesia Half')
    expect(race).toHaveTextContent('Completed')
    expect(race).toHaveTextContent('21.1 km · 1:38:00 · 4:39 /km · 90 m')
    expect(within(race).getByRole('link', { name: 'View details' })).toBeInTheDocument()
    expect(within(race).getByRole('link', { name: 'Edit' })).toBeInTheDocument()
    expect(within(race).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    // Rated and completed: neither Mark as completed nor Rate.
    expect(within(race).queryByRole('link', { name: 'Mark as completed' })).not.toBeInTheDocument()
    expect(within(race).queryByRole('link', { name: 'Rate' })).not.toBeInTheDocument()
  })

  it('offers Mark as completed for planned runs and Rate for completed unrated runs', async () => {
    const planned = await openDay('2026-11-18', 'Wednesday, November 18, 2026')
    const plannedItem = within(planned.dialog).getByRole('listitem')
    expect(plannedItem).toHaveTextContent('Planned')
    expect(within(plannedItem).getByRole('link', { name: 'Mark as completed' })).toBeInTheDocument()
    expect(within(plannedItem).queryByRole('link', { name: 'Rate' })).not.toBeInTheDocument()
    planned.unmount()

    resetFakeApi()
    await openDay('2026-11-06', 'Friday, November 6, 2026')
    expect(screen.getByRole('link', { name: 'Rate' })).toBeInTheDocument()
  })

  it('gives each action a description that names its run', async () => {
    const { dialog } = await openDay('2026-11-14', DAY_14)
    const description = within(dialog).getByRole('link', { name: 'View details' }).getAttribute('aria-describedby')!
    expect(document.getElementById(description)).toHaveTextContent('Road race · Silesia Half')
  })

  it('shows an empty state and an Add run link that carries the date', async () => {
    const { dialog } = await openDay('2026-11-10', 'Tuesday, November 10, 2026')

    expect(within(dialog).getByText('No runs on this day.')).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Add run on this day' })).toHaveAttribute(
      'href',
      '/diary/2026/11/runs/new?date=2026-11-10',
    )
  })

  it.each(['2026-02-30', '2026-13-01', 'nope'])('%s is Not found', async (date) => {
    renderRoute(`/diary/2026/11/day/${date}`)
    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument()
  })

  it('a valid date outside the visible grid is Not found too', async () => {
    renderRoute('/diary/2026/11/day/2027-03-01')
    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument()
  })

  it('shows a retry when the calendar cannot be loaded', async () => {
    getFakeApi().addFault(faults.status({ method: 'GET', path: /calendar/ }, 500))
    renderRoute('/diary/2026/11/day/2026-11-14')
    expect(await screen.findAllByRole('button', { name: 'Try again' })).not.toHaveLength(0)
  })
})

describe('Month list panel', () => {
  it('lists runs of the month by ISO week with the week totals', async () => {
    renderRoute('/diary/2026/11/list')
    const dialog = await screen.findByRole('dialog', { name: 'Runs in November 2026' })
    await loaded(dialog)

    const week45 = within(dialog).getByRole('region', { name: 'Week 45' })
    expect(week45).toHaveTextContent('Completed: 44 km · 4:10:00 · 290 m')
    expect(within(week45).getAllByRole('listitem')).toHaveLength(4)
    expect(within(dialog).getByRole('region', { name: 'Week 47' })).toHaveTextContent('Planned: 17 km')
    // Dates are shown because the list spans days.
    expect(within(week45).getAllByText(/November \d+, 2026/).length).toBeGreaterThan(0)
  })

  it('only lists runs of this month, not of the neighbouring days in the grid', async () => {
    resetFakeApi({
      guestSeed: [
        { date: '2026-10-30', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 5000 },
        { date: '2026-11-05', status: 'Completed', runTypeId: typeId('easy'), distanceMeters: 6000 },
      ],
    })
    renderRoute('/diary/2026/11/list')
    const dialog = await screen.findByRole('dialog', { name: 'Runs in November 2026' })
    await loaded(dialog)
    expect(within(dialog).getAllByRole('listitem')).toHaveLength(1)
  })

  it('has an empty state', async () => {
    resetFakeApi({ guestSeed: [] })
    renderRoute('/diary/2026/11/list')
    expect(await screen.findByText('No runs this month.')).toBeInTheDocument()
  })
})

describe('Opening, closing and Back', () => {
  it('a click on a day opens the panel and Back closes it', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')

    await user.click(cell('2026-11-14'))
    await screen.findByRole('dialog', { name: DAY_14 })
    expect(router.state.location.pathname).toBe('/diary/2026/11/day/2026-11-14')

    await act(() => router.navigate(-1))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('Forward reopens the panel', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(cell('2026-11-14'))
    await screen.findByRole('dialog')
    await act(() => router.navigate(-1))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    await act(() => router.navigate(1))

    expect(await screen.findByRole('dialog', { name: DAY_14 })).toBeInTheDocument()
  })

  it('the Close button, Escape and the overlay go back in history', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')

    await user.click(cell('2026-11-14'))
    await user.click(await screen.findByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/diary/2026/11')
    expect(router.state.historyAction).toBe('POP')

    await user.click(cell('2026-11-14'))
    await screen.findByRole('dialog')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('a deep-linked panel closes onto the month (nothing to go back to)', async () => {
    const user = userEvent.setup()
    const { router } = await openDay('2026-11-14', DAY_14)

    await user.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/diary/2026/11')
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('View details from the day panel opens the run, and Back returns to the day', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(cell('2026-11-14'))
    const dialog = await screen.findByRole('dialog', { name: DAY_14 })

    await user.click(within(dialog).getByRole('link', { name: 'View details' }))
    await screen.findByRole('dialog', { name: 'Silesia Half' })
    expect(router.state.location.pathname).toMatch(/\/diary\/2026\/11\/runs\/[0-9a-f-]+$/)

    await act(() => router.navigate(-1))
    expect(await screen.findByRole('dialog', { name: DAY_14 })).toBeInTheDocument()
  })
})

describe('Focus return (AC13)', () => {
  it('nested: closing the details returns the focus to the View details link of the day panel', async () => {
    const user = userEvent.setup()
    const { dialog } = await openDay('2026-11-14', DAY_14)
    await user.click(within(dialog).getByRole('link', { name: 'View details' }))
    const details = await screen.findByRole('dialog', { name: /Silesia Half|Road race/ })
    await loaded(details)

    await user.keyboard('{Escape}')

    const day = await screen.findByRole('dialog', { name: DAY_14 })
    await waitFor(() => expect(within(day).getByRole('link', { name: 'View details' })).toHaveFocus())
  })

  it('nested: closing the edit form returns the focus to the Edit link of the day panel', async () => {
    const user = userEvent.setup()
    const { dialog } = await openDay('2026-11-14', DAY_14)
    await user.click(within(dialog).getByRole('link', { name: 'Edit' }))
    await screen.findByRole('dialog', { name: 'Edit run' })

    await user.keyboard('{Escape}')

    const day = await screen.findByRole('dialog', { name: DAY_14 })
    await waitFor(() => expect(within(day).getByRole('link', { name: 'Edit' })).toHaveFocus())
  })

  it('nested: details -> edit -> close lands on the details Edit link', async () => {
    const user = userEvent.setup()
    const { dialog } = await openDay('2026-11-14', DAY_14)
    await user.click(within(dialog).getByRole('link', { name: 'View details' }))
    const details = await screen.findByRole('dialog', { name: /Silesia Half|Road race/ })
    await loaded(details)
    await user.click(within(details).getByRole('link', { name: 'Edit' }))
    await screen.findByRole('dialog', { name: 'Edit run' })

    await user.keyboard('{Escape}')

    const back = await screen.findByRole('dialog', { name: /Silesia Half|Road race/ })
    await waitFor(() => expect(within(back).getByRole('link', { name: 'Edit' })).toHaveFocus())
  })

  it('a deep-linked panel (closed with replace) does not carry a stale target to the next dialog', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/runs/new')
    await screen.findByRole('dialog', { name: 'Add run' })
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() => expect(document.querySelector('[role="gridcell"][tabindex="0"]')).toHaveFocus())
  })

  it('Escape returns the focus to the day cell that opened the panel', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    cell('2026-11-14').focus()

    await user.keyboard('{Enter}')
    await screen.findByRole('dialog', { name: DAY_14 })
    await user.keyboard('{Escape}')

    await waitFor(() => expect(cell('2026-11-14')).toHaveFocus())
  })

  it('the Close button returns the focus to the day', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(cell('2026-11-14'))

    await user.click(await screen.findByRole('button', { name: 'Close' }))

    await waitFor(() => expect(cell('2026-11-14')).toHaveFocus())
  })

  it("a run opened from its bubble returns the focus to the run's day", async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(cell('2026-11-14').querySelector<HTMLElement>('button[data-run-id]')!)
    await screen.findByRole('dialog', { name: 'Silesia Half' })

    await user.keyboard('{Escape}')

    await waitFor(() => expect(cell('2026-11-14')).toHaveFocus())
  })

  it("the month list falls back to the grid's tab stop", async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(screen.getByRole('link', { name: 'Month list' }))
    await screen.findByRole('dialog')

    await user.keyboard('{Escape}')

    await waitFor(() => expect(document.activeElement).toHaveAttribute('role', 'gridcell'))
  })

  it('a deep-linked panel moves the focus into the grid when it closes', async () => {
    const user = userEvent.setup()
    await openDay('2026-11-14', DAY_14)
    await user.keyboard('{Escape}')
    await waitFor(() => expect(cell('2026-11-14')).toHaveFocus())
  })
})

describe('Run details', () => {
  async function openDetails(date: string, dayTitle: string, runName: string | RegExp) {
    const user = userEvent.setup()
    const { dialog: day, ...rest } = await openDay(date, dayTitle)
    await user.click(within(day).getByRole('link', { name: 'View details' }))
    const dialog = await screen.findByRole('dialog', { name: runName })
    await loaded(dialog)
    return { user, dialog, ...rest }
  }

  it('shows a race with its details and the rating', async () => {
    const { dialog } = await openDetails('2026-11-14', DAY_14, 'Silesia Half')

    expect(await within(dialog).findByText('Silesia Half Marathon', { exact: false })).toBeInTheDocument()
    const race = within(dialog).getByRole('region', { name: /Race: Silesia Half Marathon/ })
    expect(race).toHaveTextContent('Half marathon')
    expect(race).toHaveTextContent('1:40:00') // target
    expect(race).toHaveTextContent('Overall place')
    expect(within(dialog).getByText('5 out of 5 stars')).toBeInTheDocument()
    expect(within(dialog).getByText('PB')).toBeInTheDocument()
    expect(within(dialog).getByText('4:39 /km')).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Edit' })).toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: 'Change rating' })).toBeInTheDocument()
  })

  it('shows the start time, and the zone only when it differs from the profile zone', async () => {
    const seed: RunWriteRequest[] = [
      { date: '2026-11-05', status: 'Completed', runTypeId: typeId('easy'), startTime: '07:15' },
      {
        date: '2026-11-06',
        status: 'Completed',
        runTypeId: typeId('easy'),
        startTime: '09:00',
        timeZoneId: 'Europe/London',
      },
    ]
    resetFakeApi({ guestSeed: seed })
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/day/2026-11-05')
    let day = await screen.findByRole('dialog')
    await loaded(day)
    await user.click(within(day).getByRole('link', { name: 'View details' }))
    let dialog = await screen.findByRole('dialog', { name: 'Easy run' })
    await within(dialog).findByText(/07:15/)
    expect(dialog).not.toHaveTextContent('Time zone')

    await act(() => Promise.resolve())
    // The other run lives in London.
    await user.keyboard('{Escape}')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await user.click(cell('2026-11-06'))
    day = await screen.findByRole('dialog')
    await loaded(day)
    await user.click(within(day).getByRole('link', { name: 'View details' }))
    dialog = await screen.findByRole('dialog', { name: 'Easy run' })
    expect(await within(dialog).findByText(/09:00/)).toHaveTextContent('Time zone: Europe/London')
  })

  it('offers Mark as completed for a planned run', async () => {
    const { dialog } = await openDetails('2026-11-18', 'Wednesday, November 18, 2026', 'Easy run')
    expect(await within(dialog).findByRole('link', { name: 'Mark as completed' })).toHaveAttribute(
      'href',
      expect.stringMatching(/\/runs\/[0-9a-f-]+\/edit\?complete=1$/),
    )
  })

  it('renders a results link only for http(s) with rel noopener noreferrer', async () => {
    resetFakeApi({
      guestSeed: [
        {
          date: '2026-11-14',
          status: 'Completed',
          runTypeId: typeId('roadRace'),
          race: {
            name: 'City 10K',
            distanceCategory: 'TenK',
            resultsUrl: 'https://example.org/results/1',
          },
        },
      ],
    })
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/day/2026-11-14')
    const day = await screen.findByRole('dialog')
    await loaded(day)
    await user.click(within(day).getByRole('link', { name: 'View details' }))
    const link = await screen.findByRole('link', { name: /Open results/ })
    expect(link).toHaveAttribute('href', 'https://example.org/results/1')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('an unknown run id shows a "not found" dialog that closes onto the month', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11/runs/00000000-0000-4000-8000-00000000ffff')

    const dialog = await screen.findByRole('dialog', { name: 'Run not found' })
    expect(dialog).toHaveTextContent('This item no longer exists.')
    await user.click(within(dialog).getAllByRole('button', { name: 'Close' })[0]!)

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11'))
  })
})

describe('Delete flow', () => {
  async function askToDelete(date: string, title: string, runText: string) {
    const user = userEvent.setup()
    const opened = await openDay(date, title)
    const item = itemFor(opened.dialog, runText)
    const button = within(item).getByRole('button', { name: 'Delete' })
    await user.click(button)
    const confirm = await screen.findByRole('alertdialog', { name: 'Delete this run?' })
    return { user, button, confirm, runId: item.dataset.runId!, ...opened }
  }

  it('asks for confirmation naming the run; Cancel keeps it and returns the focus to the button', async () => {
    const { user, button, confirm } = await askToDelete('2026-11-14', DAY_14, 'Silesia Half')
    expect(confirm).toHaveTextContent(
      'Road race on Saturday, November 14, 2026 will be deleted. This cannot be undone.',
    )

    await user.click(within(confirm).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(button).toHaveFocus()
    expect(storedRuns().some((run) => run.title === 'Silesia Half')).toBe(true)
  })

  it('Delete removes the run (DELETE), shows a toast, and the list and the grid update', async () => {
    const requests = (() => {
      const list = recordRequests()
      return list
    })()
    const { user, confirm } = await askToDelete('2026-11-14', DAY_14, 'Silesia Half')

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByText('Run deleted.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(requests.some((r) => r.method === 'DELETE' && /\/api\/runs\//.test(r.path))).toBe(true)
    expect(storedRuns().some((run) => run.title === 'Silesia Half')).toBe(false)
    const day = screen.getByRole('dialog', { name: DAY_14 })
    await waitFor(() => expect(within(day).getByText('No runs on this day.')).toBeInTheDocument())
    // The grid behind the panel is refreshed (it is aria-hidden, so check the DOM directly).
    expect(cell('2026-11-14').querySelector('[data-run-id]')).toBeNull()
    // The delete button is gone with its run: the focus moves to the panel title.
    expect(document.activeElement).toHaveAttribute('data-slot', 'dialog-title')
  })

  it('stays open with a message when the server fails, and works on retry', async () => {
    getFakeApi().addFault(faults.status({ method: 'DELETE', path: /^\/api\/runs\// }, 503, 1))
    const { user, confirm } = await askToDelete('2026-11-14', DAY_14, 'Silesia Half')

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    expect(await within(confirm).findByRole('alert')).toHaveTextContent('The server is not reachable right now.')
    expect(storedRuns().some((run) => run.title === 'Silesia Half')).toBe(true)

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(storedRuns().some((run) => run.title === 'Silesia Half')).toBe(false)
  })

  it('treats 404 (already deleted elsewhere) as done', async () => {
    const { user, confirm, runId } = await askToDelete('2026-11-14', DAY_14, 'Silesia Half')
    await deleteElsewhere(runId)

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByText('This run no longer exists.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  })

  it('Delete from the details closes the panel and returns to the grid day', async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(cell('2026-11-14').querySelector<HTMLElement>('button[data-run-id]')!)
    const details = await screen.findByRole('dialog', { name: 'Silesia Half' })
    await user.click(await within(details).findByRole('button', { name: 'Delete' }))
    const confirm = await screen.findByRole('alertdialog')

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/diary/2026/11'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getAllByText('Run deleted.').length).toBeGreaterThan(0)
    await waitFor(() => expect(cell('2026-11-14')).toHaveFocus())
    expect(cell('2026-11-14').querySelector('[data-run-id]')).toBeNull()
  })
})

describe('Phone sheet (AC14)', () => {
  it('renders panels as bottom sheets on phones and as dialogs otherwise', async () => {
    renderRoute('/diary/2026/11/day/2026-11-14')
    const dialog = await screen.findByRole('dialog', { name: DAY_14 })
    expect(dialog).toHaveAttribute('data-variant', 'dialog')

    setPhoneViewport(true)
    expect(screen.getByRole('dialog', { name: DAY_14 })).toHaveAttribute('data-variant', 'sheet')
    expect(screen.getByRole('dialog', { name: DAY_14 }).className).toContain('bottom-0')

    setPhoneViewport(false)
    expect(screen.getByRole('dialog', { name: DAY_14 })).toHaveAttribute('data-variant', 'dialog')
  })

  it('keeps tap targets at least 44 px on phones', async () => {
    renderRoute('/diary/2026/11/day/2026-11-14')
    const dialog = await screen.findByRole('dialog', { name: DAY_14 })
    for (const control of [...within(dialog).getAllByRole('link'), ...within(dialog).getAllByRole('button')]) {
      expect(control.className).toMatch(/\bh-11\b|\bsize-11\b/)
    }
  })
})
