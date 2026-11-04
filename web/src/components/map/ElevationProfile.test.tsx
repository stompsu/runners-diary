import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import { gliwice10kGeoJson } from '@/test/fixtures/tracks'
import ElevationProfile from './ElevationProfile'

const profile = gliwice10kGeoJson.profile!

beforeAll(() => {
  // Recharts' ResponsiveContainer observes its size; jsdom has no ResizeObserver (it renders at `initialDimension`).
  vi.stubGlobal(
    'ResizeObserver',
    class {
      callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback
      }
      observe(target: Element) {
        this.callback([{ target, contentRect: { width: 600, height: 160 } } as ResizeObserverEntry], this as never)
      }
      unobserve() {}
      disconnect() {}
    },
  )
})

/** Mirrors TrackViewer: the parent owns the active index. */
function Harness({ onHover }: { onHover?: (index: number | null) => void }) {
  const [index, setIndex] = useState<number | null>(null)
  return (
    <ElevationProfile
      profile={profile}
      activeIndex={index}
      onHover={(next) => {
        setIndex(next)
        onHover?.(next)
      }}
    />
  )
}

const slider = () => screen.getByRole('slider', { name: /^Elevation profile/ })

describe('ElevationProfile', () => {
  it('draws the chart and exposes one labelled slider over all samples', () => {
    const { container } = render(<Harness />)
    expect(slider()).toHaveAttribute('min', '0')
    expect(slider()).toHaveAttribute('max', String(profile.length - 1))
    expect(container.querySelector('svg.recharts-surface')).not.toBeNull()
  })

  it('starts at the beginning on focus and reads the position as text', async () => {
    const hover = vi.fn<(index: number | null) => void>()
    render(<Harness onHover={hover} />)
    await userEvent.tab()
    expect(slider()).toHaveFocus()
    expect(hover).toHaveBeenLastCalledWith(0)
    expect(slider()).toHaveAttribute('aria-valuetext', expect.stringMatching(/^0 km · 214 m$/))
  })

  it('moves along the route with the keyboard and reports every index', async () => {
    const hover = vi.fn<(index: number | null) => void>()
    render(<Harness onHover={hover} />)
    await userEvent.tab()
    await userEvent.keyboard('{ArrowRight}{ArrowRight}{ArrowRight}')
    expect(hover).toHaveBeenLastCalledWith(3)
    await userEvent.keyboard('{ArrowLeft}')
    expect(hover).toHaveBeenLastCalledWith(2)
    await userEvent.keyboard('{End}')
    expect(hover).toHaveBeenLastCalledWith(profile.length - 1)
    expect(slider()).toHaveAttribute('aria-valuetext', expect.stringContaining('10.01 km'))
    await userEvent.keyboard('{Home}')
    expect(hover).toHaveBeenLastCalledWith(0)
  })

  it('clears the position on Escape and when focus leaves', async () => {
    const hover = vi.fn<(index: number | null) => void>()
    render(
      <>
        <Harness onHover={hover} />
        <button type="button">After</button>
      </>,
    )
    await userEvent.tab()
    await userEvent.keyboard('{ArrowRight}')
    await userEvent.keyboard('{Escape}')
    expect(hover).toHaveBeenLastCalledWith(null)
    await userEvent.keyboard('{ArrowRight}')
    expect(hover).toHaveBeenLastCalledWith(0)
    await userEvent.tab()
    expect(hover).toHaveBeenLastCalledWith(null)
  })

  it('maps the pointer position inside the plot area to the nearest sample', async () => {
    const hover = vi.fn<(index: number | null) => void>()
    const { container } = render(<Harness onHover={hover} />)
    const surface = container.querySelector('input')!.parentElement as HTMLElement
    // 600 px wide chart: the plot spans x = 44 .. 592 (Y axis 44 px, 8 px right margin).
    surface.getBoundingClientRect = () =>
      ({ left: 0, top: 0, width: 600, height: 160, right: 600, bottom: 160 }) as DOMRect

    await userEvent.pointer({ target: surface, coords: { clientX: 318 } }) // exactly the middle of the plot
    expect(hover).toHaveBeenLastCalledWith(300)
    await userEvent.pointer({ target: surface, coords: { clientX: 2 } }) // left of the plot: clamps to the start
    expect(hover).toHaveBeenLastCalledWith(0)
    await userEvent.pointer({ target: surface, coords: { clientX: 599 } }) // right of the plot: clamps to the end
    expect(hover).toHaveBeenLastCalledWith(profile.length - 1)
  })

  it('clears the position when a mouse leaves but keeps it after a touch ends', () => {
    const hover = vi.fn<(index: number | null) => void>()
    const { container } = render(<Harness onHover={hover} />)
    const surface = container.querySelector('input')!.parentElement as HTMLElement
    hover.mockClear()
    const leave = (pointerType: string) => fireEvent.pointerLeave(surface, { pointerType })
    leave('touch')
    expect(hover).not.toHaveBeenCalled()
    leave('mouse')
    expect(hover).toHaveBeenCalledWith(null)
  })
})
