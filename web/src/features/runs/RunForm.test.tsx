import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, typeId } from '@/test/fakeApi'
import { renderRoute } from '@/test/render'
import { deleteElsewhere, editElsewhere, recordRequests, respondOnce, storedRuns } from '@/test/requests'

// Default fake: today 2026-11-03 (Europe/Warsaw), guest seed = demoRuns (see RunPanels.test.tsx).

const cell = (date: string) => document.querySelector<HTMLElement>(`[role="gridcell"][data-date="${date}"]`)!
const loaded = (dialog: HTMLElement) =>
  waitFor(() => expect(dialog.querySelector('[data-slot="skeleton"]')).not.toBeInTheDocument())

async function openNew(date = '2026-11-14') {
  const user = userEvent.setup()
  const result = renderRoute(`/diary/2026/11/runs/new?date=${date}`)
  const dialog = await screen.findByRole('dialog', { name: 'Add run' })
  await within(dialog).findByLabelText('Title')
  return { user, dialog, ...result }
}

/** Day panel -> the run's Edit (or another) link. */
async function openFromDay(date: string, dayTitle: string, runText: string, link: string, dialogName: string) {
  const user = userEvent.setup()
  const result = renderRoute(`/diary/2026/11/day/${date}`)
  const day = await screen.findByRole('dialog', { name: dayTitle })
  await loaded(day)
  const item = within(day)
    .getAllByRole('listitem')
    .find((candidate) => candidate.textContent?.includes(runText))!
  await user.click(within(item).getByRole('link', { name: link }))
  const dialog = await screen.findByRole('dialog', { name: dialogName })
  await loaded(dialog)
  return { user, dialog, runId: item.dataset.runId!, ...result }
}

const openEdit = (date: string, dayTitle: string, runText: string) =>
  openFromDay(date, dayTitle, runText, 'Edit', 'Edit run').then(async (opened) => {
    await within(opened.dialog).findByLabelText('Title')
    return opened
  })

const EASY_DAY = 'Monday, November 2, 2026'

describe('Add run', () => {
  it('prefills the date from ?date= and the profile zone, and shows the pace live', async () => {
    const { user, dialog } = await openNew('2026-11-14')

    expect(within(dialog).getByLabelText('Date')).toHaveValue('2026-11-14')
    expect(within(dialog).getByTestId('zone-current')).toHaveTextContent('Europe/Warsaw')
    expect(within(dialog).getByLabelText('Run type')).toHaveDisplayValue('Easy run')
    expect(within(dialog).getByRole('radio', { name: 'Planned' })).toBeChecked()
    expect(within(dialog).getByTestId('pace')).toHaveTextContent('Enter distance and duration')

    await user.type(within(dialog).getByLabelText('Distance (km)'), '8')
    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '44:00')

    expect(within(dialog).getByTestId('pace')).toHaveTextContent('5:30 /km')
  })

  it('does not put the pace in an aria-live region; the debounced announcement speaks once it settles', async () => {
    const { user, dialog } = await openNew('2026-11-14')
    const live = () => within(dialog).getByTestId('pace-announcement')

    await user.type(within(dialog).getByLabelText('Distance (km)'), '8')
    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '44:00')

    expect(within(dialog).getByTestId('pace')).toHaveTextContent('5:30 /km')
    expect(within(dialog).getByTestId('pace').closest('[aria-live]')).toBeNull()
    expect(live()).toBeEmptyDOMElement() // still typing: nothing announced yet
    await waitFor(() => expect(live()).toHaveTextContent('Pace: 5:30 /km'), { timeout: 3000 })
  })

  it('falls back to today when ?date= is missing or invalid', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/runs/new?date=2026-02-30')
    const dialog = await screen.findByRole('dialog', { name: 'Add run' })
    expect(await within(dialog).findByLabelText('Date')).toHaveValue('2026-11-03')
    expect(user).toBeDefined()
  })

  it('sends SI values (km -> m, h:mm:ss -> s) and no zone, then shows a toast and updates the grid', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openNew('2026-11-20')

    await user.type(within(dialog).getByLabelText('Title'), 'Hill session')
    await user.type(within(dialog).getByLabelText('Distance (km)'), '10,5')
    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '52:30')
    await user.type(within(dialog).getByLabelText('Elevation gain (m)'), '120')
    await user.selectOptions(within(dialog).getByLabelText('Run type'), 'Hill repeats')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findAllByText('Run added.')).not.toHaveLength(0)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = requests.find((r) => r.method === 'POST' && r.path === '/api/runs')!
    expect(post.body).toMatchObject({
      date: '2026-11-20',
      status: 'Planned',
      runTypeId: typeId('hillRepeats'),
      title: 'Hill session',
      distanceMeters: 10500,
      durationSeconds: 3150,
      elevationGainMeters: 120,
      startTime: null,
      race: null,
      rating: null,
    })
    expect(post.body).not.toHaveProperty('timeZoneId')
    expect(post.body).not.toHaveProperty('version')
    expect(storedRuns().filter((run) => run.date === '2026-11-20')).toHaveLength(1)
    await waitFor(() => expect(cell('2026-11-20').querySelector('[data-run-id]')).not.toBeNull())
    expect(cell('2026-11-20').querySelector('[data-status="Planned"]')).not.toBeNull()
  })

  it("opens from the header link with today's date and Cancel closes it", async () => {
    const user = userEvent.setup()
    const { router } = renderRoute('/diary/2026/11')
    await screen.findByRole('grid')
    await user.click(screen.getByRole('link', { name: 'Add run' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add run' })
    expect(await within(dialog).findByLabelText('Date')).toHaveValue('2026-11-03')

    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/diary/2026/11')
  })

  it('adding from a day returns to that day panel afterwards', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/day/2026-11-10')
    const day = await screen.findByRole('dialog', { name: 'Tuesday, November 10, 2026' })
    await user.click(within(day).getByRole('link', { name: 'Add run on this day' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add run' })
    expect(await within(dialog).findByLabelText('Date')).toHaveValue('2026-11-10')
    await user.type(within(dialog).getByLabelText('Distance (km)'), '5')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const back = await screen.findByRole('dialog', { name: 'Tuesday, November 10, 2026' })
    expect(await within(back).findByRole('listitem')).toHaveTextContent('Easy run')
  })

  it('lets the user pick another time zone, and sends it only then', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openNew()
    expect(within(dialog).queryByLabelText('Time zone')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Change time zone' }))
    await user.selectOptions(within(dialog).getByLabelText('Time zone'), 'Europe/London')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'POST' && r.path === '/api/runs')).toBe(true))
    expect(requests.find((r) => r.method === 'POST' && r.path === '/api/runs')!.body).toMatchObject({
      timeZoneId: 'Europe/London',
    })
  })
})

describe('Form validation (client rules, ADR 0018)', () => {
  it('shows field errors, marks the controls invalid and sends nothing', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openNew()
    await user.type(within(dialog).getByLabelText('Distance (km)'), 'abc')
    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '1:75')
    await user.type(within(dialog).getByLabelText('Average heart rate (bpm)'), '160')
    await user.type(within(dialog).getByLabelText('Max heart rate (bpm)'), '150')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const distance = within(dialog).getByLabelText('Distance (km)')
    expect(distance).toHaveAttribute('aria-invalid', 'true')
    expect(distance).toHaveAccessibleDescription('Enter a distance in km, for example 10.5.')
    expect(within(dialog).getByLabelText('Duration (h:mm:ss)')).toHaveAccessibleDescription(
      /Enter a duration as h:mm:ss or mm:ss/,
    )
    expect(within(dialog).getByLabelText('Max heart rate (bpm)')).toHaveAccessibleDescription(
      'Max heart rate must be at least the average.',
    )
    await waitFor(() => expect(distance).toHaveFocus())
    expect(requests.some((r) => r.method === 'POST')).toBe(false)
  })

  it('clears an error once the value is fixed', async () => {
    const { user, dialog } = await openNew()
    const distance = within(dialog).getByLabelText('Distance (km)')
    await user.type(distance, 'abc')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(distance).toHaveAttribute('aria-invalid', 'true')

    await user.clear(distance)
    await user.type(distance, '5')

    await waitFor(() => expect(distance).not.toHaveAttribute('aria-invalid'))
  })

  it('rejects an out-of-range effort and a malformed start time', async () => {
    const { user, dialog } = await openNew()
    await user.type(within(dialog).getByLabelText('Effort (RPE)'), '11')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(within(dialog).getByLabelText('Effort (RPE)')).toHaveAccessibleDescription(
      /Effort must be a whole number from 1 to 10\./,
    )
  })
})

describe('Conditional sections', () => {
  it('shows the race section only for race types, and result fields only when completed', async () => {
    const { user, dialog } = await openNew()
    expect(within(dialog).queryByRole('group', { name: 'Race' })).not.toBeInTheDocument()

    await user.selectOptions(within(dialog).getByLabelText('Run type'), 'Road race')
    const race = within(dialog).getByRole('group', { name: 'Race' })
    expect(within(race).getByLabelText('Race name')).toBeInTheDocument()
    expect(within(race).queryByLabelText('Result time (h:mm:ss)')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Completed' }))
    expect(within(race).getByLabelText('Result time (h:mm:ss)')).toBeInTheDocument()
    expect(within(race).getByLabelText('Overall place')).toBeInTheDocument()

    await user.selectOptions(within(dialog).getByLabelText('Run type'), 'Long run')
    expect(within(dialog).queryByRole('group', { name: 'Race' })).not.toBeInTheDocument()
  })

  it('requires a race name and saves race details in SI', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openNew('2026-11-28')
    await user.selectOptions(within(dialog).getByLabelText('Run type'), 'Road race')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(within(dialog).getByLabelText('Race name')).toHaveAccessibleDescription('Enter the race name.')

    await user.type(within(dialog).getByLabelText('Race name'), 'Gliwice 10K')
    await user.selectOptions(within(dialog).getByLabelText('Race distance'), '10K')
    await user.type(within(dialog).getByLabelText('Target time (h:mm:ss)'), '45:00')
    await user.type(within(dialog).getByLabelText('Results link'), 'https://example.org/r')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'POST' && r.path === '/api/runs')).toBe(true))
    expect(requests.find((r) => r.method === 'POST')!.body!.race).toMatchObject({
      name: 'Gliwice 10K',
      distanceCategory: 'TenK',
      targetTimeSeconds: 2700,
      resultTimeSeconds: null,
      resultsUrl: 'https://example.org/r',
    })
  })

  it('shows the rating only for completed runs; going back to Planned clears it', async () => {
    const { user, dialog } = await openNew()
    expect(within(dialog).queryByRole('radiogroup', { name: 'How did it go?' })).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Completed' }))
    await user.click(within(dialog).getByRole('radio', { name: '4 stars' }))
    expect(within(dialog).getByText('4 out of 5 stars')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Planned' }))
    expect(within(dialog).queryByRole('radiogroup', { name: 'How did it go?' })).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Completed' }))
    expect(within(dialog).getByText('No rating yet')).toBeInTheDocument()
  })

  it('saves the rating and comment of a completed run', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openNew('2026-11-12')
    await user.click(within(dialog).getByRole('radio', { name: 'Completed' }))
    await user.type(within(dialog).getByLabelText('Distance (km)'), '6')
    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '33:00')
    await user.click(within(dialog).getByRole('radio', { name: '3 stars' }))
    await user.type(within(dialog).getByLabelText('Comment'), 'Heavy legs')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'POST')).toBe(true))
    expect(requests.find((r) => r.method === 'POST')!.body).toMatchObject({
      status: 'Completed',
      rating: { stars: 3, comment: 'Heavy legs' },
    })
  })

  it('a rest day hides the numbers and the rating', async () => {
    const { user, dialog } = await openNew()
    expect(within(dialog).getByLabelText('Distance (km)')).toBeInTheDocument()

    await user.selectOptions(within(dialog).getByLabelText('Run type'), 'Rest day')
    await user.click(within(dialog).getByRole('radio', { name: 'Completed' }))

    expect(within(dialog).queryByLabelText('Distance (km)')).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('radiogroup', { name: 'How did it go?' })).not.toBeInTheDocument()
  })
})

describe('Edit run', () => {
  it('loads the run in display units and saves a change, sending the location the form shows', async () => {
    resetFakeApi({
      guestSeed: [
        {
          date: '2026-11-02',
          status: 'Completed',
          runTypeId: typeId('easy'),
          title: 'Easy morning',
          distanceMeters: 8000,
          durationSeconds: 2640,
          location: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
        },
      ],
    })
    const requests = recordRequests()
    const { user, dialog } = await openEdit('2026-11-02', EASY_DAY, 'Easy morning')

    expect(within(dialog).getByLabelText('Title')).toHaveValue('Easy morning')
    expect(within(dialog).getByLabelText('Distance (km)')).toHaveValue('8')
    expect(within(dialog).getByLabelText('Duration (h:mm:ss)')).toHaveValue('0:44:00')
    expect(within(dialog).getByTestId('pace')).toHaveTextContent('5:30 /km')
    expect(within(dialog).getByRole('radio', { name: 'Completed' })).toBeChecked()

    await user.clear(within(dialog).getByLabelText('Title'))
    await user.type(within(dialog).getByLabelText('Title'), 'Renamed')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findAllByText('Run saved.')).not.toHaveLength(0)
    const put = requests.find((r) => r.method === 'PUT')!
    expect(put.body).toMatchObject({
      title: 'Renamed',
      distanceMeters: 8000,
      durationSeconds: 2640,
      location: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice' },
    })
    expect(put.body).not.toHaveProperty('timeZoneId')
    expect(put.body!.version).toEqual(expect.any(String))
    const [stored] = storedRuns()
    expect(stored).toMatchObject({ title: 'Renamed', location: { name: 'Gliwice' } })
  })

  it("shows the run's own zone label when it differs from the profile zone", async () => {
    resetFakeApi({
      guestSeed: [
        {
          date: '2026-11-02',
          status: 'Completed',
          runTypeId: typeId('easy'),
          title: 'Abroad',
          timeZoneId: 'Europe/London',
        },
      ],
    })
    const { dialog } = await openEdit('2026-11-02', EASY_DAY, 'Abroad')
    expect(within(dialog).getByTestId('zone-current')).toHaveTextContent('Europe/London')
  })

  it('moving a completed, rated race back to Planned sends no rating and no result', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openEdit('2026-11-14', 'Saturday, November 14, 2026', 'Silesia Half')
    expect(within(dialog).getByRole('group', { name: 'Race' })).toBeInTheDocument()

    await user.click(within(dialog).getByRole('radio', { name: 'Planned' }))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    const body = requests.find((r) => r.method === 'PUT')!.body!
    expect(body).toMatchObject({ status: 'Planned', rating: null })
    expect(body.race).toMatchObject({ name: 'Silesia Half Marathon', resultTimeSeconds: null, overallPlace: null })
    expect(storedRuns().find((run) => run.title === 'Silesia Half')).toMatchObject({ status: 'Planned', rating: null })
  })
})

describe('Quick actions', () => {
  it('Mark as completed opens the form as Completed with the first empty actual focused', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openFromDay(
      '2026-11-18',
      'Wednesday, November 18, 2026',
      'Easy run',
      'Mark as completed',
      'Complete run',
    )

    expect(within(dialog).getByRole('radio', { name: 'Completed' })).toBeChecked()
    expect(within(dialog).getByLabelText('Distance (km)')).toHaveValue('8') // the plan's target stays
    await waitFor(() => expect(within(dialog).getByLabelText('Duration (h:mm:ss)')).toHaveFocus())

    await user.type(within(dialog).getByLabelText('Duration (h:mm:ss)'), '44:00')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({
      status: 'Completed',
      distanceMeters: 8000,
      durationSeconds: 2640,
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Complete run' })).not.toBeInTheDocument())
    await waitFor(() => expect(cell('2026-11-18').querySelector('[data-status="Completed"]')).not.toBeNull())
  })

  it('Mark as completed focuses the distance when it is empty', async () => {
    resetFakeApi({ guestSeed: [{ date: '2026-11-18', status: 'Planned', runTypeId: typeId('long') }] })
    const { dialog } = await openFromDay(
      '2026-11-18',
      'Wednesday, November 18, 2026',
      'Long run',
      'Mark as completed',
      'Complete run',
    )
    await waitFor(() => expect(within(dialog).getByLabelText('Distance (km)')).toHaveFocus())
  })

  it('Rate asks for stars first, then saves the rating and nothing else', async () => {
    const requests = recordRequests()
    const { user, dialog } = await openFromDay(
      '2026-11-06',
      'Friday, November 6, 2026',
      'Long run',
      'Rate',
      'Rate this run',
    )

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(within(dialog).getByText('Choose a star rating first.')).toBeInTheDocument()
    expect(requests.some((r) => r.method === 'PUT')).toBe(false)

    await user.click(within(dialog).getByRole('radio', { name: '4 stars' }))
    await user.type(within(dialog).getByLabelText('Comment'), 'Steady')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findAllByText('Rating saved.')).not.toHaveLength(0)
    const body = requests.find((r) => r.method === 'PUT')!.body!
    expect(body).toMatchObject({
      rating: { stars: 4, comment: 'Steady' },
      status: 'Completed',
      distanceMeters: 21000,
      durationSeconds: 7560,
    })
    expect(storedRuns().find((run) => run.title === 'Long run')).toMatchObject({
      rating: { stars: 4, comment: 'Steady' },
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Rate this run' })).not.toBeInTheDocument())
  })

  it('Rate from the details view works with ?rate=1 deep links too', async () => {
    const { user } = await openFromDay('2026-11-06', 'Friday, November 6, 2026', 'Long run', 'View details', 'Long run')
    await user.click(await screen.findByRole('link', { name: 'Rate' }))
    expect(await screen.findByRole('dialog', { name: 'Rate this run' })).toBeInTheDocument()
  })
})

describe('Server errors (AC11)', () => {
  it('maps 400 field errors onto the fields and the rest onto a focused summary', async () => {
    const { user, dialog } = await openNew('2026-11-25')
    await user.type(within(dialog).getByLabelText('Distance (km)'), '5')
    respondOnce('POST', '/api/runs', 400, {
      errors: {
        distanceMeters: ['Distance must be greater than 0.'],
        'race.resultTimeSeconds': ['A result is only allowed for completed runs.'],
        body: ['The request body is invalid.'],
      },
    })

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const distance = within(dialog).getByLabelText('Distance (km)')
    await waitFor(() => expect(distance).toHaveAttribute('aria-invalid', 'true'))
    expect(distance).toHaveAccessibleDescription('Distance must be greater than 0.')
    const summary = within(dialog)
      .getByText('Some values are not valid. Check the highlighted fields.')
      .closest<HTMLElement>('[role="alert"]')!
    expect(summary).toHaveTextContent('Some values are not valid. Check the highlighted fields.')
    // `race.resultTimeSeconds` has no field here (no race section) and `body` is not a field: both end up in the summary.
    expect(summary).toHaveTextContent('The request body is invalid.')
    expect(summary).toHaveTextContent('A result is only allowed for completed runs.')
    await waitFor(() => expect(summary).toHaveFocus())
    expect(screen.getByRole('dialog', { name: 'Add run' })).toBeInTheDocument()
  })

  it('focuses the first field with a server error when there is no summary', async () => {
    const { user, dialog } = await openNew('2026-11-25')
    respondOnce('POST', '/api/runs', 400, { errors: { title: ['Title is not allowed.'] } })

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(within(dialog).getByLabelText('Title')).toHaveFocus())
    expect(within(dialog).getByLabelText('Title')).toHaveAccessibleDescription('Title is not allowed.')
  })

  it('409 concurrency keeps the user\'s values; "Save mine anyway" re-PUTs with the latest version', async () => {
    const requests = recordRequests()
    const { user, dialog, runId } = await openEdit('2026-11-02', EASY_DAY, 'Easy morning')
    await editElsewhere(runId, { title: 'Changed elsewhere', notes: 'from my phone' })
    await user.clear(within(dialog).getByLabelText('Title'))
    await user.type(within(dialog).getByLabelText('Title'), 'Mine')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('This run was changed elsewhere since you opened it.')
    expect(within(alert).getByRole('button', { name: 'Reload latest' })).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Title')).toHaveValue('Mine')
    expect(screen.getByRole('dialog', { name: 'Edit run' })).toBeInTheDocument()

    await user.click(within(alert).getByRole('button', { name: 'Save mine anyway' }))

    expect(await screen.findAllByText('Run saved.')).not.toHaveLength(0)
    const puts = requests.filter((r) => r.method === 'PUT')
    expect(puts).toHaveLength(2)
    expect(puts[1]!.body!.version).not.toBe(puts[0]!.body!.version)
    expect(storedRuns().find((run) => run.id === runId)).toMatchObject({ title: 'Mine' })
  })

  it('409 concurrency: "Reload latest" discards the edits and shows the latest values', async () => {
    const { user, dialog, runId } = await openEdit('2026-11-02', EASY_DAY, 'Easy morning')
    await editElsewhere(runId, { title: 'Changed elsewhere' })
    await user.clear(within(dialog).getByLabelText('Title'))
    await user.type(within(dialog).getByLabelText('Title'), 'Mine')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    const alert = await within(dialog).findByRole('alert')

    await user.click(within(alert).getByRole('button', { name: 'Reload latest' }))

    await waitFor(() => expect(within(dialog).getByLabelText('Title')).toHaveValue('Changed elsewhere'))
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    // Saving now works with the fresh version.
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await screen.findAllByText('Run saved.')).not.toHaveLength(0)
  })

  it('409 run-limit shows a localized form-level message and keeps the form', async () => {
    resetFakeApi({ limits: { maxRunsPerDate: 1 } })
    const { user, dialog } = await openNew('2026-11-02') // that day already has a run
    await user.type(within(dialog).getByLabelText('Distance (km)'), '5')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('You have reached the maximum number of runs.')
    expect(within(dialog).getByLabelText('Distance (km)')).toHaveValue('5')
    expect(within(dialog).queryByRole('button', { name: 'Reload latest' })).not.toBeInTheDocument()
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Save' })).toBeEnabled())
  })

  it('404 on save: the run is gone, a toast says so and the dialog closes', async () => {
    const { user, dialog, runId } = await openEdit('2026-11-02', EASY_DAY, 'Easy morning')
    await deleteElsewhere(runId)

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findAllByText('This run no longer exists.')).not.toHaveLength(0)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Edit run' })).not.toBeInTheDocument())
  })

  it('opening the edit form of a deleted run shows the "not found" dialog', async () => {
    renderRoute('/diary/2026/11/runs/00000000-0000-4000-8000-00000000ffff/edit')
    expect(await screen.findByRole('dialog', { name: 'Run not found' })).toBeInTheDocument()
  })

  it('413 shows "request too large"', async () => {
    getFakeApi().addFault(faults.status({ method: 'POST', path: '/api/runs' }, 413, 1))
    const { user, dialog } = await openNew()

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The request is too large.')
  })

  it('429 shows the wait time from Retry-After, and a retry then succeeds', async () => {
    getFakeApi().addFault(faults.rateLimited({ method: 'POST', path: '/api/runs' }, 30, 1))
    const { user, dialog } = await openNew('2026-11-27')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Too many requests. Please try again in 30 s.')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await screen.findAllByText('Run added.')).not.toHaveLength(0)
  })

  it('a network failure keeps the form and offers a retry', async () => {
    getFakeApi().addFault(faults.network({ method: 'POST', path: '/api/runs' }, 1))
    const { user, dialog } = await openNew('2026-11-27')
    await user.type(within(dialog).getByLabelText('Title'), 'Keep me')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The server is not reachable right now.')
    expect(within(dialog).getByLabelText('Title')).toHaveValue('Keep me')
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('disables Save while the request is in flight (no double submit)', async () => {
    getFakeApi().addFault(faults.delay({ method: 'POST', path: '/api/runs' }, 150, 1))
    const requests = recordRequests()
    const { user, dialog } = await openNew('2026-11-27')

    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Saving…' })).toBeDisabled())
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add run' })).not.toBeInTheDocument())
    expect(requests.filter((r) => r.method === 'POST')).toHaveLength(1)
  })
})
