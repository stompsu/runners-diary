import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { signInForTest } from '@/test/render'
import { getFakeApi } from '@/test/fake'
import { downloadFileName, downloadTrackFile } from './downloadTrack'

describe('downloadFileName', () => {
  it.each([
    ['gliwice-10k.gpx', 'gliwice-10k.gpx'],
    ['Morning run.GPX', 'Morning run.GPX'],
    ['../../etc/passwd', '.._.._etc_passwd.gpx'],
    ['a:b*c?.gpx', 'a_b_c_.gpx'],
    ['', 'track.gpx'],
    ['..', 'track.gpx'],
    ['no-extension', 'no-extension.gpx'],
  ])('%j -> %j', (input, expected) => {
    expect(downloadFileName(input)).toBe(expected)
  })
})

describe('downloadTrackFile', () => {
  const click = vi.fn<() => void>()
  beforeEach(() => {
    URL.createObjectURL = vi.fn<() => string>(() => 'blob:fake')
    URL.revokeObjectURL = vi.fn<() => void>()
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(click)
  })
  afterEach(() => vi.restoreAllMocks())

  it('fetches the file with the bearer token and hands it to the browser under its own name', async () => {
    const token = signInForTest('guest')
    const run = getFakeApi().runsOf(token)[0]!
    const track = getFakeApi().addTrack(token, run.id, { kind: 'RecordedTrack', fileName: 'my run.gpx' })
    let download: string | undefined
    click.mockImplementation(() => (download = document.querySelector('a[download]')?.getAttribute('download') ?? ''))

    await downloadTrackFile(run.id, track)

    expect(click).toHaveBeenCalledTimes(1)
    expect(download).toBe('my run.gpx')
    expect(URL.createObjectURL).toHaveBeenCalledWith(expect.any(Blob))
    expect(document.querySelector('a[download]')).toBeNull()
  })

  it('revokes the object URL only after a delay, not right after the click', async () => {
    vi.useFakeTimers()
    try {
      const token = signInForTest('guest')
      const run = getFakeApi().runsOf(token)[0]!
      const track = getFakeApi().addTrack(token, run.id, { kind: 'RecordedTrack', fileName: 'a.gpx' })
      await downloadTrackFile(run.id, track)

      await vi.advanceTimersByTimeAsync(5000)
      expect(URL.revokeObjectURL).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(5000)
      expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:fake')
    } finally {
      vi.useRealTimers()
    }
  })

  it('throws the problem when the file is gone', async () => {
    const token = signInForTest('guest')
    const run = getFakeApi().runsOf(token)[0]!
    await expect(downloadTrackFile(run.id, { id: 'nope', originalFileName: 'x.gpx' })).rejects.toMatchObject({
      status: 404,
    })
  })
})
