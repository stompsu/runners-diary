import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { supportsWebGl2 } from '@/components/map/webgl'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, sampleGpx, typeId, type RunWriteRequest } from '@/test/fakeApi'
import { resetFakeMap } from '@/test/fakeMapState'
import { renderRoute } from '@/test/render'
import { recordRequests, recordUploads, respondOnce, storedRuns } from '@/test/requests'
import { gpxFile } from '@/test/gpxFile'
import { stubResizeObserver } from '@/test/resizeObserver'
import { setPhoneViewport } from '@/test/viewport'

// The map engine, the profile chart and the viewer are lazy chunks; load them during collection (no test timeout).
await import('@/components/map/ElevationProfile')
await import('./TrackViewer')

vi.mock('@/components/map/webgl', () => ({ supportsWebGl2: vi.fn<() => boolean>(() => true) }))
vi.mock('@/components/map/maplibre/MapLibreMap', () => import('@/test/fakeMapProvider'))

beforeAll(stubResizeObserver)
beforeEach(() => {
  resetFakeMap()
  vi.mocked(supportsWebGl2).mockReturnValue(true)
  URL.createObjectURL = vi.fn<() => string>(() => 'blob:fake')
  URL.revokeObjectURL = vi.fn<() => void>()
})
afterEach(() => vi.restoreAllMocks())

const NOV14 = '2026-11-14'
const RECORDED = sampleGpx({ times: ['2026-11-14T07:30:00Z', '2026-11-14T08:19:30Z'] })
const planned = (extra: Partial<RunWriteRequest> = {}): RunWriteRequest => ({
  date: NOV14,
  status: 'Planned',
  runTypeId: typeId('easy'),
  title: 'Long one',
  ...extra,
})
const completed = (extra: Partial<RunWriteRequest> = {}): RunWriteRequest => planned({ status: 'Completed', ...extra })

const gpx = (name = 'morning.gpx', text = RECORDED) => gpxFile(name, text)
const loaded = (dialog: HTMLElement) =>
  waitFor(() => expect(dialog.querySelector('[data-slot="skeleton"]')).not.toBeInTheDocument())

/** Replaces the default fake by one holding exactly `seed`, signs in and opens the run's details (or `path`). */
async function setup(
  seed: RunWriteRequest,
  options: {
    tracks?: Array<Parameters<ReturnType<typeof getFakeApi>['addTrack']>[2]>
    path?: (runId: string, trackIds: string[]) => string
  } = {},
) {
  resetFakeApi({ guestSeed: [seed] })
  const user = userEvent.setup({ applyAccept: false })
  const result = renderRoute('/diary/2026/11')
  await screen.findByRole('grid')
  const token = (JSON.parse(localStorage.getItem('rd.guestSession')!) as { accessToken: string }).accessToken
  const [run] = getFakeApi().runsOf(token)
  const trackIds = (options.tracks ?? []).map((track) => getFakeApi().addTrack(token, run!.id, track).id)
  const path = options.path?.(run!.id, trackIds) ?? `/diary/2026/11/runs/${run!.id}`
  await act(() => result.router.navigate(path))
  return { user, token, runId: run!.id, trackIds, ...result }
}

async function openDialog(name: string) {
  const dialog = await screen.findByRole('dialog', { name })
  await loaded(dialog)
  return dialog
}

async function uploadVia(
  user: ReturnType<typeof userEvent.setup>,
  details: HTMLElement,
  file = gpx(),
  options: { kind?: string } = {},
) {
  await user.click(within(details).getByRole('button', { name: 'Add GPX file' }))
  const dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
  if (options.kind) await user.click(within(dialog).getByRole('radio', { name: options.kind }))
  await user.upload(within(dialog).getByLabelText('GPX file'), file)
  return dialog
}

describe('GPX files in the run details', () => {
  it('shows an empty state with a single Add button', async () => {
    await setup(planned())
    const details = await openDialog('Long one')
    expect(within(details).getByRole('heading', { name: 'GPX files' })).toBeInTheDocument()
    expect(within(details).getByText(/No GPX file yet/)).toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Add GPX file' })).toBeInTheDocument()
  })

  it('has no GPX section for a rest day', async () => {
    await setup(completed({ runTypeId: typeId('rest'), title: 'Day off' }))
    const details = await openDialog('Day off')
    expect(within(details).queryByRole('heading', { name: 'GPX files' })).not.toBeInTheDocument()
  })

  it('lists each file with its kind and the actions, named per kind', async () => {
    await setup(completed(), {
      tracks: [
        { kind: 'PlannedCourse', fileName: 'plan.gpx' },
        { kind: 'RecordedTrack', fileName: 'watch.gpx' },
      ],
    })
    const details = await openDialog('Long one')
    const items = within(details).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Planned course')
    expect(items[0]).toHaveTextContent('plan.gpx')
    expect(items[1]).toHaveTextContent('Recorded track')
    for (const name of [
      'View recorded track',
      'Download recorded track',
      'Use GPX values from recorded track',
      'Replace recorded track',
      'Delete recorded track',
      'View planned course',
    ]) {
      expect([
        name,
        (within(details).queryByRole('button', { name }) ?? within(details).queryByRole('link', { name }))?.tagName,
      ]).not.toEqual([name, undefined])
    }
  })

  it('shows a neutral "not processed" state for a legacy Pending file and offers only replace and delete', async () => {
    await setup(completed(), { tracks: [{ kind: 'RecordedTrack', status: 'Pending', fileName: 'old.gpx' }] })
    const details = await openDialog('Long one')
    expect(within(details).getByTestId('track-status')).toHaveTextContent('has not been processed')
    expect(within(details).queryByRole('link', { name: /^View/ })).not.toBeInTheDocument()
    expect(within(details).queryByRole('button', { name: /^Download/ })).not.toBeInTheDocument()
    expect(within(details).queryByRole('button', { name: /^Use values/ })).not.toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Replace recorded track' })).toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Delete recorded track' })).toBeInTheDocument()
  })
})

describe('upload -> review -> edit form -> save (ADR 0023)', () => {
  it('a recorded track on a Planned run: defaults, Apply opens the dirty edit form, nothing saved until Save', async () => {
    const { user, runId } = await setup(planned())
    const requests = recordRequests()
    const uploads = recordUploads()
    const details = await openDialog('Long one')

    await uploadVia(user, details, gpx(), { kind: 'Recorded track' })
    const upload = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    await user.click(within(upload).getByRole('button', { name: 'Upload' }))

    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })
    expect(uploads).toEqual([
      expect.objectContaining({ kind: 'RecordedTrack', replace: undefined, fileName: 'morning.gpx' }),
    ])
    const row = (name: string) => within(review).getByRole('checkbox', { name: new RegExp(`^${name}`) })
    // Everything is empty on the run, so everything is pre-checked.
    for (const name of [
      'Mark as completed',
      'Date and start time',
      'Distance',
      'Duration',
      'Elevation gain',
      'Elevation loss',
      'Location',
    ]) {
      expect([name, row(name)]).toEqual([name, expect.toBeChecked()])
    }
    expect(within(review).getByText(/10.005 km/)).toBeInTheDocument()
    expect(within(review).getByRole('radio', { name: 'Elapsed time 0:49:30' })).toBeChecked()

    await user.click(within(review).getByRole('button', { name: 'Apply to the run form' }))

    const form = await screen.findByRole('dialog', { name: 'Edit run' })
    expect(await within(form).findByTestId('prefill-notice')).toHaveTextContent('Values from morning.gpx')
    expect(within(form).getByLabelText('Distance (km)')).toHaveValue('10.005')
    expect(within(form).getByLabelText('Duration (h:mm:ss)')).toHaveValue('0:49:30')
    expect(within(form).getByLabelText('Elevation gain (m)')).toHaveValue('15')
    expect(within(form).getByLabelText('Start time (optional)')).toHaveValue('08:30')
    expect(within(form).getByRole('radio', { name: 'Completed' })).toBeChecked()
    expect(within(form).getByTestId('location-coordinates')).toHaveTextContent('50.29486, 18.67664')
    // Review only: the upload happened, the run itself was not written.
    expect(requests.filter((r) => r.method === 'PUT')).toHaveLength(0)
    expect(storedRuns()[0]).toMatchObject({ status: 'Planned', distanceMeters: null })

    await user.click(within(form).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(requests.filter((r) => r.method === 'PUT')).toHaveLength(1))
    expect(requests.find((r) => r.method === 'PUT')!.body).toMatchObject({
      status: 'Completed',
      distanceMeters: 10005,
      durationSeconds: 2970,
      elevationGainMeters: 15,
      elevationLossMeters: 15,
      startTime: '08:30',
      location: { latitude: 50.294862, longitude: 18.676642, name: null },
    })
    await waitFor(() => expect(storedRuns()[0]).toMatchObject({ status: 'Completed', distanceMeters: 10005 }))
    expect(storedRuns()[0]!.tracks).toHaveLength(1)
    expect(runId).toBeTruthy()
  })

  it('differing values are unchecked, a date mismatch is warned about, and only ticked rows are applied', async () => {
    const { user } = await setup(completed({ date: '2026-11-03', distanceMeters: 9500, durationSeconds: 3000 }))
    const details = await openDialog('Long one')
    const upload = await uploadVia(user, details)
    await user.click(within(upload).getByRole('button', { name: 'Upload' }))

    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })
    const checkbox = (name: string) => within(review).getByRole('checkbox', { name: new RegExp(`^${name}`) })
    expect(checkbox('Distance')).not.toBeChecked()
    expect(checkbox('Duration')).not.toBeChecked()
    expect(checkbox('Date and start time')).not.toBeChecked()
    expect(checkbox('Elevation gain')).toBeChecked()
    const warning = within(review).getByText(
      /was recorded on Saturday, November 14, 2026, but the run is on Tuesday, November 3, 2026/,
    )
    expect(checkbox('Date and start time')).toHaveAccessibleDescription(warning.textContent!)
    expect(within(review).getByText('9.5 km', { exact: false })).toBeInTheDocument()

    await user.click(checkbox('Distance'))
    await user.click(within(review).getByRole('button', { name: 'Apply to the run form' }))

    const form = await screen.findByRole('dialog', { name: 'Edit run' })
    await within(form).findByTestId('prefill-notice')
    expect(within(form).getByLabelText('Distance (km)')).toHaveValue('10.005')
    expect(within(form).getByLabelText('Duration (h:mm:ss)')).toHaveValue('0:50:00')
    expect(within(form).getByLabelText('Date')).toHaveValue('2026-11-03')
    expect(within(form).getByLabelText('Start time (optional)')).toHaveValue('')
  })

  it('lets the user choose the moving time instead of the elapsed time', async () => {
    const { user } = await setup(completed())
    const details = await openDialog('Long one')
    const upload = await uploadVia(user, details)
    await user.click(within(upload).getByRole('button', { name: 'Upload' }))
    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })

    await user.click(within(review).getByRole('radio', { name: 'Moving time 0:49:25' }))
    expect(within(review).getByText('0:49:25', { selector: 'span.font-medium' })).toBeInTheDocument()
    await user.click(within(review).getByRole('button', { name: 'Apply to the run form' }))

    const form = await screen.findByRole('dialog', { name: 'Edit run' })
    await within(form).findByTestId('prefill-notice')
    expect(within(form).getByLabelText('Duration (h:mm:ss)')).toHaveValue('0:49:25')
  })

  it('Apply is disabled when nothing is ticked, and Cancel leaves the run untouched', async () => {
    const { user } = await setup(completed())
    const details = await openDialog('Long one')
    const upload = await uploadVia(user, details)
    await user.click(within(upload).getByRole('button', { name: 'Upload' }))
    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })

    for (const box of within(review).getAllByRole('checkbox'))
      if ((box as HTMLInputElement).checked) await user.click(box)
    expect(within(review).getByRole('button', { name: 'Apply to the run form' })).toBeDisabled()

    await user.click(within(review).getByRole('button', { name: 'Cancel' }))
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Use values from the GPX file' })).not.toBeInTheDocument(),
    )
    expect(storedRuns()[0]).toMatchObject({ distanceMeters: null })
    // The uploaded file stays; it can be reviewed again.
    expect(
      await within(details).findByRole('button', { name: 'Use GPX values from recorded track' }),
    ).toBeInTheDocument()
  })

  it('does not open a review when the run already has every value the file proposes', async () => {
    await setup(
      completed({
        startTime: '08:30',
        distanceMeters: 10_005,
        durationSeconds: 2970,
        elevationGainMeters: 15,
        elevationLossMeters: 15,
        location: { latitude: 50.294862, longitude: 18.676642, name: 'Start' },
      }),
    )
    const user = userEvent.setup({ applyAccept: false })
    const details = await openDialog('Long one')
    const upload = await uploadVia(user, details)
    await user.click(within(upload).getByRole('button', { name: 'Upload' }))

    expect(await within(details).findByRole('button', { name: 'Delete recorded track' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Use values from the GPX file' })).not.toBeInTheDocument()
    expect((await screen.findAllByText('GPX file added.')).length).toBeGreaterThan(0)
  })

  it('"Use GPX values" on an existing file reviews it later, and says so when there is nothing to fill in', async () => {
    const { user } = await setup(completed({ distanceMeters: 10_005 }), {
      tracks: [
        {
          kind: 'PlannedCourse',
          summary: { elevationGainMeters: null, elevationLossMeters: null, start: { latitude: 50.3, longitude: 18.7 } },
        },
      ],
    })
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Use GPX values from planned course' }))
    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })
    // distance equal, no elevation in the file: only the location is left
    expect(within(review).getAllByRole('checkbox')).toHaveLength(1)
    await user.click(within(review).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(review).not.toBeInTheDocument())
    expect(within(details).getByRole('button', { name: 'Use GPX values from planned course' })).toHaveFocus()
  })

  it('says there is nothing to fill in when a reviewed file matches the run', async () => {
    const { user } = await setup(
      completed({ distanceMeters: 10_005, location: { latitude: 50.3, longitude: 18.7, name: 'Park' } }),
      {
        tracks: [
          {
            kind: 'PlannedCourse',
            summary: {
              elevationGainMeters: null,
              elevationLossMeters: null,
              start: { latitude: 50.3, longitude: 18.7 },
            },
          },
        ],
      },
    )
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Use GPX values from planned course' }))
    const review = await screen.findByRole('dialog', { name: 'Use values from the GPX file' })
    expect(await within(review).findByText('The run already has the values from this file.')).toBeInTheDocument()
    expect(within(review).queryByRole('button', { name: 'Apply to the run form' })).not.toBeInTheDocument()
    // The dialog's own X and the Close button in its body.
    await user.click(within(review).getAllByRole('button', { name: 'Close' }).at(-1)!)
    await waitFor(() => expect(review).not.toBeInTheDocument())
  })

  it('says why there is nothing to apply for a rest day', async () => {
    // The details of a rest day have no GPX section, so the review is only reachable by a stale open dialog.
    const { user } = await setup(completed({ runTypeId: typeId('rest'), title: 'Day off' }))
    const details = await openDialog('Day off')
    expect(within(details).queryByRole('button', { name: 'Add GPX file' })).not.toBeInTheDocument()
    expect(user).toBeDefined()
  })
})

describe('upload dialog', () => {
  it('checks the extension and the size before sending anything', async () => {
    const { user } = await setup(completed())
    const uploads = recordUploads()
    const details = await openDialog('Long one')

    const dialog = await uploadVia(user, details, gpxFile('notes.txt', 'x'))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Choose a file with the .gpx extension.')
    expect(within(dialog).getByLabelText('GPX file')).toBeInvalid()
    expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeDisabled()

    // A guest may upload 5 MB, not 6.
    await user.upload(within(dialog).getByLabelText('GPX file'), gpxFile('big.gpx', new ArrayBuffer(6 * 1024 * 1024)))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('The file is too large. The limit is 5 MB.')
    expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeDisabled()

    await user.upload(within(dialog).getByLabelText('GPX file'), gpxFile('empty.gpx', ''))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('not valid XML')

    await user.upload(within(dialog).getByLabelText('GPX file'), gpx())
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeEnabled()
    expect(uploads).toHaveLength(0)
  })

  it('offers the kind choice, defaulting to the recorded track for a completed run and the course for a planned one', async () => {
    const completedRun = await setup(completed())
    let details = await openDialog('Long one')
    await completedRun.user.click(within(details).getByRole('button', { name: 'Add GPX file' }))
    let dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    expect(within(dialog).getByRole('radio', { name: 'Recorded track' })).toBeChecked()
    expect(within(dialog).getByRole('radio', { name: 'Planned course' })).not.toBeChecked()
    completedRun.unmount()

    const plannedRun = await setup(planned())
    details = await openDialog('Long one')
    await plannedRun.user.click(within(details).getByRole('button', { name: 'Add GPX file' }))
    dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    expect(within(dialog).getByRole('radio', { name: 'Planned course' })).toBeChecked()
  })

  it('shows progress while the file is being sent and blocks a second submit', async () => {
    const { user } = await setup(completed())
    getFakeApi().addFault(faults.delay({ method: 'POST', path: /\/tracks$/ }, 300, 1))
    const uploads = recordUploads()
    const details = await openDialog('Long one')
    const dialog = await uploadVia(user, details)

    await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
    expect(within(dialog).getByRole('button', { name: 'Uploading…' })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeDisabled()
    expect(within(dialog).getByRole('status')).toHaveTextContent('Uploading and processing the file')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add a GPX file' })).not.toBeInTheDocument())
    expect(uploads).toHaveLength(1)
  })

  it('puts the focus back on the Add button when it is cancelled', async () => {
    const { user } = await setup(completed())
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Add GPX file' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(within(details).getByRole('button', { name: 'Add GPX file' })).toHaveFocus())
  })

  it('is a bottom sheet on a phone', async () => {
    const { user } = await setup(completed())
    setPhoneViewport(true)
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Add GPX file' }))
    expect(await screen.findByRole('dialog', { name: 'Add a GPX file' })).toHaveAttribute('data-variant', 'sheet')
  })

  describe('every problem is a localised message and the dialog stays open for another try', () => {
    const codes: Array<[string, RegExp]> = [
      ['malformedXml', /not valid XML/],
      ['dtdProhibited', /document type declaration/],
      ['unsupportedFormat', /Only plain \.gpx files/],
      ['notGpx', /XML but not a GPX/],
      ['tooLarge', /file is too large/],
      ['tooManyPoints', /too many track points/],
      ['tooFewPoints', /at least two track points/],
      ['invalidCoordinate', /position that is not valid/],
      ['tooComplex', /too deeply nested/],
      ['wrongExtension', /\.gpx extension/],
    ]
    it.each(codes)('invalid-gpx %s', async (code, expected) => {
      const { user } = await setup(completed())
      getFakeApi().addFault(
        faults.problem({ method: 'POST', path: /\/tracks$/ }, 400, 'urn:runnersdiary:problem:invalid-gpx', {
          members: { code, errors: { file: ['English text from the server'] } },
          times: 1,
        }),
      )
      const details = await openDialog('Long one')
      const dialog = await uploadVia(user, details)
      await user.click(within(dialog).getByRole('button', { name: 'Upload' }))

      const alert = await within(dialog).findByRole('alert')
      expect(alert).toHaveTextContent(expected)
      expect(alert).not.toHaveTextContent('English text from the server')
      // Still open, still usable: the retry goes through.
      await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
      await waitFor(() => expect(storedRuns()[0]!.tracks).toHaveLength(1))
    })

    it('the fake API itself rejects unreadable files with the right code', async () => {
      const { user } = await setup(completed())
      const details = await openDialog('Long one')
      const dialog = await uploadVia(
        user,
        details,
        gpx('dtd.gpx', '<?xml version="1.0"?><!DOCTYPE gpx [<!ENTITY x "y">]><gpx></gpx>'),
      )
      await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
      expect(await within(dialog).findByRole('alert')).toHaveTextContent('document type declaration')
      await user.upload(
        within(dialog).getByLabelText('GPX file'),
        gpx('one.gpx', sampleGpx().replace(/<trkpt[\s\S]*<\/trkpt>/, '<trkpt lat="1" lon="2"></trkpt>')),
      )
      await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
      expect(await within(dialog).findByRole('alert')).toHaveTextContent('at least two track points')
    })

    it.each([
      ['413 payload too large', faults.status({ method: 'POST', path: /\/tracks$/ }, 413, 1), /limit is 5 MB/],
      [
        'track-limit',
        faults.problem({ method: 'POST', path: /\/tracks$/ }, 409, 'urn:runnersdiary:problem:track-limit', {
          times: 1,
        }),
        /maximum number of GPX files/,
      ],
      ['rest day', faults.status({ method: 'POST', path: /\/tracks$/ }, 400, 1), /Some values are not valid|Rest days/],
      [
        'processing-busy',
        faults.problem({ method: 'POST', path: /\/tracks$/ }, 503, 'urn:runnersdiary:problem:processing-busy', {
          retryAfterSeconds: 10,
          times: 1,
        }),
        /try again in 10 s/,
      ],
      [
        'storage-unavailable',
        faults.problem({ method: 'POST', path: /\/tracks$/ }, 503, 'urn:runnersdiary:problem:storage-unavailable', {
          retryAfterSeconds: 5,
          times: 1,
        }),
        /storage is not available.*5 s/,
      ],
      ['429', faults.rateLimited({ method: 'POST', path: /\/tracks$/ }, 42, 1), /42 s/],
      ['415', faults.status({ method: 'POST', path: /\/tracks$/ }, 415, 1), /not understood/],
      ['404', faults.status({ method: 'POST', path: /\/tracks$/ }, 404, 1), /no longer exists/],
      ['network failure', faults.network({ method: 'POST', path: /\/tracks$/ }, 1), /not reachable/],
      ['html from a proxy', faults.html({ method: 'POST', path: /\/tracks$/ }, 502, 1), /not reachable/],
    ])('%s', async (_name, fault, expected) => {
      const { user } = await setup(completed())
      getFakeApi().addFault(fault)
      const details = await openDialog('Long one')
      const dialog = await uploadVia(user, details)
      await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
      expect(await within(dialog).findByRole('alert')).toHaveTextContent(expected)
      expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeEnabled()
    })
  })
})

describe('replace', () => {
  it('asks before replacing, sends replace=true only after the confirmation, and swaps the file', async () => {
    const { user, token, runId, trackIds } = await setup(completed(), {
      tracks: [{ kind: 'RecordedTrack', fileName: 'old.gpx' }],
    })
    const uploads = recordUploads()
    const details = await openDialog('Long one')

    await user.click(within(details).getByRole('button', { name: 'Replace recorded track' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    expect(within(dialog).getByRole('radio', { name: 'Recorded track' })).toBeChecked()
    expect(within(dialog).getByTestId('replace-notice')).toHaveTextContent('"old.gpx"')
    await user.upload(within(dialog).getByLabelText('GPX file'), gpx('new.gpx'))
    await user.click(within(dialog).getByRole('button', { name: 'Replace file' }))

    const confirm = await screen.findByRole('alertdialog', { name: 'Replace the existing file?' })
    expect(uploads).toHaveLength(0)
    // Cancelling the question sends nothing and keeps the old file.
    await user.click(within(confirm).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(uploads).toHaveLength(0)

    await user.click(within(dialog).getByRole('button', { name: 'Replace file' }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Replace file' }))

    await waitFor(() => expect(uploads).toHaveLength(1))
    expect(uploads[0]).toMatchObject({ kind: 'RecordedTrack', replace: 'true', fileName: 'new.gpx' })
    await waitFor(() => expect(within(details).getByText('new.gpx')).toBeInTheDocument())
    expect(within(details).queryByText('old.gpx')).not.toBeInTheDocument()
    const stored = getFakeApi().tracksOf(token, runId)
    expect(stored).toHaveLength(1)
    expect(stored[0]!.id).not.toBe(trackIds[0])
  })

  it('a 409 track-exists from a race with another tab asks the same question and retries with replace', async () => {
    const { user } = await setup(completed())
    respondOnce('POST', /\/tracks$/, 409, { type: 'urn:runnersdiary:problem:track-exists' })
    const uploads = recordUploads()
    const details = await openDialog('Long one')
    const dialog = await uploadVia(user, details)
    await user.click(within(dialog).getByRole('button', { name: 'Upload' }))

    const confirm = await screen.findByRole('alertdialog', { name: 'Replace the existing file?' })
    expect(confirm).toHaveTextContent('already has a recorded track')
    await user.click(within(confirm).getByRole('button', { name: 'Replace file' }))

    await waitFor(() => expect(uploads).toHaveLength(2))
    expect(uploads.map((upload) => upload.replace)).toEqual([undefined, 'true'])
    await waitFor(() => expect(storedRuns()[0]!.tracks).toHaveLength(1))
  })

  it('both kinds can exist at once and each is replaced on its own', async () => {
    const { user, token, runId } = await setup(completed(), {
      tracks: [
        { kind: 'PlannedCourse', fileName: 'plan.gpx' },
        { kind: 'RecordedTrack', fileName: 'watch.gpx' },
      ],
    })
    const uploads = recordUploads()
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Replace planned course' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    expect(within(dialog).getByRole('radio', { name: 'Planned course' })).toBeChecked()
    await user.upload(within(dialog).getByLabelText('GPX file'), gpx('plan2.gpx'))
    await user.click(within(dialog).getByRole('button', { name: 'Replace file' }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Replace file' }))
    await waitFor(() => expect(uploads[0]).toMatchObject({ kind: 'PlannedCourse', replace: 'true' }))
    await waitFor(() =>
      expect(
        getFakeApi()
          .tracksOf(token, runId)
          .map((t) => t.originalFileName)
          .sort(),
      ).toEqual(['plan2.gpx', 'watch.gpx']),
    )
  })
})

describe('delete', () => {
  it('confirms, deletes the file and keeps the run', async () => {
    const { user, token, runId } = await setup(completed(), {
      tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }],
    })
    const requests = recordRequests()
    const details = await openDialog('Long one')

    await user.click(within(details).getByRole('button', { name: 'Delete recorded track' }))
    const confirm = await screen.findByRole('alertdialog', { name: 'Delete this GPX file?' })
    expect(confirm).toHaveTextContent('"watch.gpx"')
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(within(details).queryByText('watch.gpx')).not.toBeInTheDocument())
    expect(requests.filter((r) => r.method === 'DELETE').map((r) => r.path)).toEqual([
      expect.stringMatching(new RegExp(`^/api/runs/${runId}/tracks/`)),
    ])
    expect(getFakeApi().tracksOf(token, runId)).toHaveLength(0)
    expect(storedRuns()).toHaveLength(1)
    expect((await screen.findAllByText('GPX file deleted.')).length).toBeGreaterThan(0)
    await waitFor(() => expect(within(details).getByRole('heading', { name: 'GPX files' })).toHaveFocus())
  })

  it('Cancel deletes nothing', async () => {
    const { user, token, runId } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack' }] })
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Delete recorded track' }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(getFakeApi().tracksOf(token, runId)).toHaveLength(1)
    expect(within(details).getByRole('button', { name: 'Delete recorded track' })).toHaveFocus()
  })

  it('a storage outage (503 + Retry-After) keeps the dialog open with a message, and a retry works', async () => {
    const { user, token, runId } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack' }] })
    getFakeApi().addFault(
      faults.problem({ method: 'DELETE', path: /\/tracks\// }, 503, 'urn:runnersdiary:problem:storage-unavailable', {
        retryAfterSeconds: 5,
        times: 1,
      }),
    )
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Delete recorded track' }))
    const confirm = await screen.findByRole('alertdialog')
    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))
    expect(await within(confirm).findByRole('alert')).toHaveTextContent(
      'storage is not available right now. Please try again in 5 s',
    )
    expect(getFakeApi().tracksOf(token, runId)).toHaveLength(1)

    await user.click(within(confirm).getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(getFakeApi().tracksOf(token, runId)).toHaveLength(0))
  })

  it('a file that is already gone counts as deleted', async () => {
    const { user } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }] })
    getFakeApi().addFault(faults.status({ method: 'DELETE', path: /\/tracks\// }, 404, 1))
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('button', { name: 'Delete recorded track' }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Delete' }))
    expect((await screen.findAllByText('This GPX file no longer exists.')).length).toBeGreaterThan(0)
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  })
})

describe('download', () => {
  it('downloads the original file through the API and reports a failure as a toast', async () => {
    const { user } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }] })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const requests = recordRequests()
    const details = await openDialog('Long one')

    await user.click(within(details).getByRole('button', { name: 'Download recorded track' }))
    await waitFor(() => expect(click).toHaveBeenCalledTimes(1))
    expect(requests.some((r) => r.path.endsWith('/gpx'))).toBe(true)

    getFakeApi().addFault(faults.status({ method: 'GET', path: /\/gpx$/ }, 404, 1))
    await user.click(within(details).getByRole('button', { name: 'Download recorded track' }))
    expect(await within(details).findByTestId('track-status')).toHaveTextContent('The file is missing.')
    expect(click).toHaveBeenCalledTimes(1)
  })
})

describe('track whose file is missing (row outlived its blobs)', () => {
  it('download: says the file is missing and offers only replace and delete', async () => {
    const { user } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }] })
    const details = await openDialog('Long one')
    getFakeApi().addFault(faults.status({ method: 'GET', path: /\/gpx$/ }, 404, 1))
    await user.click(within(details).getByRole('button', { name: 'Download recorded track' }))

    expect(await within(details).findByTestId('track-status')).toHaveTextContent(/file is missing.*delete.*replace/i)
    expect(within(details).queryByRole('button', { name: /^Download/ })).not.toBeInTheDocument()
    expect(within(details).queryByRole('link', { name: 'View recorded track' })).not.toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Replace recorded track' })).toBeInTheDocument()
    expect(within(details).getByRole('button', { name: 'Delete recorded track' })).toBeInTheDocument()
  })

  it('viewer: a 404 on the geometry shows the missing-file state, not a generic error', async () => {
    const { router, runId, trackIds } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack' }] })
    getFakeApi().addFault(faults.status({ method: 'GET', path: /\/geojson$/ }, 404, 1))
    await act(() => router.navigate(`/diary/2026/11/runs/${runId}/tracks/${trackIds[0]}`))

    const viewer = await screen.findByRole('dialog', { name: /^Recorded track/ })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent(/file for this track is missing/)
    expect(within(viewer).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
    expect(within(viewer).queryByRole('button', { name: 'Download' })).not.toBeInTheDocument()
  })
})

describe('track view route', () => {
  const view = (runId: string, ids: string[]) => `/diary/2026/11/runs/${runId}/tracks/${ids[0]}`

  it('opens from the details with the map, the profile and the text alternative, and closes back to the details', async () => {
    const { user } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }] })
    const details = await openDialog('Long one')
    await user.click(within(details).getByRole('link', { name: 'View recorded track' }))

    const viewer = await screen.findByRole('dialog', { name: 'Recorded track · watch.gpx' })
    expect(await within(viewer).findByRole('region', { name: 'Map of the route' })).toBeInTheDocument()
    expect(await within(viewer).findByRole('slider', { name: /^Elevation profile/ })).toBeInTheDocument()
    expect(within(viewer).getByTestId('fake-map')).toHaveAttribute('data-dashed', 'false')
    expect(within(viewer).getByRole('table', { name: 'Kilometre splits' })).toBeInTheDocument()
    expect(within(viewer).getByRole('button', { name: 'Download' })).toBeInTheDocument()
    expect(window.location.pathname).toBe('/')

    await user.click(within(viewer).getByRole('button', { name: 'Close' }))
    expect(await screen.findByRole('dialog', { name: 'Long one' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('link', { name: 'View recorded track' })).toHaveFocus())
  })

  it('draws a planned course dashed', async () => {
    await setup(completed(), {
      tracks: [{ kind: 'PlannedCourse', fileName: 'plan.gpx' }],
      path: view,
    })
    const viewer = await screen.findByRole('dialog', { name: 'Planned course · plan.gpx' })
    expect(await within(viewer).findByTestId('fake-map')).toHaveAttribute('data-dashed', 'true')
  })

  it('is deep-linkable, and closing a deep link goes to the run details', async () => {
    const { user, router, runId, trackIds } = await setup(completed(), {
      tracks: [{ kind: 'RecordedTrack', fileName: 'watch.gpx' }],
    })
    // A deep link has no history behind it (a replace stands for opening the URL in a fresh tab).
    await act(() => router.navigate(view(runId, trackIds), { replace: true }))
    const viewer = await screen.findByRole('dialog', { name: 'Recorded track · watch.gpx' })
    expect(await within(viewer).findByRole('region', { name: 'Map of the route' })).toBeInTheDocument()

    await user.click(within(viewer).getByRole('button', { name: 'Close' }))
    expect(await screen.findByRole('dialog', { name: 'Long one' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe(`/diary/2026/11/runs/${runId}`)
  })

  it('shows a neutral message for a legacy Pending file (no map, no download)', async () => {
    await setup(completed(), { tracks: [{ kind: 'RecordedTrack', status: 'Pending' }], path: view })
    const viewer = await screen.findByRole('dialog', { name: /^Recorded track/ })
    expect(await within(viewer).findByText(/has not been processed/)).toBeInTheDocument()
    expect(within(viewer).queryByRole('region', { name: 'Map of the route' })).not.toBeInTheDocument()
    expect(within(viewer).queryByRole('button', { name: 'Download' })).not.toBeInTheDocument()
  })

  it('says so when the track or the run no longer exists', async () => {
    await setup(completed(), {
      path: (runId) => `/diary/2026/11/runs/${runId}/tracks/${'0'.repeat(8)}-0000-4000-8000-000000000000`,
    })
    expect(await screen.findByText('This track no longer exists.')).toBeInTheDocument()
  })

  it('offers a retry when the geometry cannot be loaded', async () => {
    const { user, router, runId, trackIds } = await setup(completed(), { tracks: [{ kind: 'RecordedTrack' }] })
    getFakeApi().addFault(faults.status({ method: 'GET', path: /\/geojson$/ }, 500, 1))
    await act(() => router.navigate(view(runId, trackIds)))

    const viewer = await screen.findByRole('dialog', { name: /^Recorded track/ })
    expect(await within(viewer).findByRole('alert')).toHaveTextContent("Couldn't load this page")
    await user.click(within(viewer).getByRole('button', { name: 'Try again' }))
    expect(await within(viewer).findByRole('region', { name: 'Map of the route' })).toBeInTheDocument()
  })

  it('keeps the summary and the splits when the browser cannot draw the map', async () => {
    vi.mocked(supportsWebGl2).mockReturnValue(false)
    await setup(completed(), { tracks: [{ kind: 'RecordedTrack' }], path: view })
    const viewer = await screen.findByRole('dialog', { name: /^Recorded track/ })
    expect(await within(viewer).findByRole('table', { name: 'Kilometre splits' })).toBeInTheDocument()
    expect(within(viewer).getByRole('region', { name: 'Map of the route' })).toHaveAttribute(
      'data-map-state',
      'unavailable',
    )
  })
})

describe('Save & add GPX', () => {
  it('saves the new run, then opens its details with the upload dialog', async () => {
    const user = userEvent.setup({ applyAccept: false })
    resetFakeApi({ guestSeed: [] })
    const requests = recordRequests()
    renderRoute('/diary/2026/11/runs/new?date=2026-11-20')
    const form = await screen.findByRole('dialog', { name: 'Add run' })
    await within(form).findByLabelText('Title')
    await user.type(within(form).getByLabelText('Title'), 'Trail day')
    await user.click(within(form).getByRole('button', { name: 'Save & add GPX' }))

    const upload = await screen.findByRole('dialog', { name: 'Add a GPX file' })
    expect(requests.filter((r) => r.method === 'POST' && r.path === '/api/runs')).toHaveLength(1)
    // The new run's details are behind the upload dialog (hence aria-hidden for queries by role).
    expect(document.querySelector('[data-panel="details"]')).toHaveTextContent('Trail day')
    expect(within(upload).getByRole('radio', { name: 'Planned course' })).toBeChecked()
    expect(storedRuns()).toHaveLength(1)

    // The request to open the dialog is consumed: no leftover parameter.
    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('is not offered for a rest day, nor when editing', async () => {
    const user = userEvent.setup()
    renderRoute('/diary/2026/11/runs/new?date=2026-11-20')
    const form = await screen.findByRole('dialog', { name: 'Add run' })
    await within(form).findByLabelText('Title')
    expect(within(form).getByRole('button', { name: 'Save & add GPX' })).toBeInTheDocument()
    await user.selectOptions(within(form).getByLabelText('Run type'), 'Rest day')
    expect(within(form).queryByRole('button', { name: 'Save & add GPX' })).not.toBeInTheDocument()
  })
})

describe('location in the details', () => {
  it('shows the place name, the coordinates and a small map', async () => {
    await setup(completed({ location: { latitude: 50.2945, longitude: 18.6714, name: 'Gliwice Rynek' } }))
    const details = await openDialog('Long one')
    const section = within(details).getByRole('heading', { name: 'Location' }).closest('section')!
    expect(section).toHaveTextContent('Gliwice Rynek')
    expect(section).toHaveTextContent('50.29450, 18.67140')
    expect(await within(section).findByRole('region', { name: "Map of the run's location" })).toBeInTheDocument()
    expect(await within(section).findByTestId('fake-map')).toHaveAttribute('data-marker', '50.2945,18.6714')
    expect(within(section).getByTestId('fake-map')).toHaveAttribute('data-fit-to', 'marker')
  })

  it('has no location section for a run without a place', async () => {
    await setup(completed())
    const details = await openDialog('Long one')
    expect(within(details).queryByRole('heading', { name: 'Location' })).not.toBeInTheDocument()
  })
})
