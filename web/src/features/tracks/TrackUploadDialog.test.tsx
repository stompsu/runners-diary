import { act, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { getFakeApi, resetFakeApi } from '@/test/fake'
import { faults, typeId } from '@/test/fakeApi'
import { gpxFile } from '@/test/gpxFile'
import { renderRoute } from '@/test/render'
import { stubResizeObserver } from '@/test/resizeObserver'

// The server's limits come from client-config: pretend it advertises 1 MB for guests.
vi.mock('@/auth/useClientConfig', async (importActual) => {
  const actual = await importActual<typeof import('@/auth/useClientConfig')>()
  return {
    useClientConfig: () => {
      const result = actual.useClientConfig()
      return result.data
        ? {
            ...result,
            data: { ...result.data, gpx: { maxFileBytesUser: 8 * 1024 * 1024, maxFileBytesGuest: 1024 * 1024 } },
          }
        : result
    },
  }
})

beforeAll(stubResizeObserver)

async function openUpload() {
  resetFakeApi({ guestSeed: [{ date: '2026-11-14', status: 'Planned', runTypeId: typeId('easy'), title: 'Long one' }] })
  const user = userEvent.setup({ applyAccept: false })
  const { router } = renderRoute('/diary/2026/11')
  await screen.findByRole('grid')
  const token = (JSON.parse(localStorage.getItem('rd.guestSession')!) as { accessToken: string }).accessToken
  const [run] = getFakeApi().runsOf(token)
  await act(() => router.navigate(`/diary/2026/11/runs/${run!.id}`))
  const details = await screen.findByRole('dialog', { name: 'Long one' })
  await user.click(await within(details).findByRole('button', { name: 'Add GPX file' }))
  return { user, dialog: await screen.findByRole('dialog', { name: 'Add a GPX file' }), token, runId: run!.id }
}

describe('upload dialog limits and races', () => {
  it('takes the size limit for the hint and the pre-check from client-config', async () => {
    const { user, dialog } = await openUpload()
    expect(within(dialog).getByText(/up to 1 MB/i)).toBeInTheDocument()

    await user.upload(within(dialog).getByLabelText('GPX file'), gpxFile('big.gpx', new ArrayBuffer(2 * 1024 * 1024)))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('The file is too large. The limit is 1 MB.')
    expect(within(dialog).getByRole('button', { name: 'Upload' })).toBeDisabled()
  })

  it('says the run or file is gone when the server answers 404', async () => {
    const { user, dialog } = await openUpload()
    getFakeApi().addFault(faults.status({ method: 'POST', path: /\/tracks$/ }, 404, 1))
    await user.upload(within(dialog).getByLabelText('GPX file'), gpxFile('a.gpx', '<gpx/>'))
    await user.click(within(dialog).getByRole('button', { name: 'Upload' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('This run or file no longer exists.')
  })
})
