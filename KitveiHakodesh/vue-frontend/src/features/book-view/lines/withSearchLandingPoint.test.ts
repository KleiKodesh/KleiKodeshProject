import { describe, it, expect } from 'vitest'
import { withSearchLandingPoint } from './useBookViewLineRenderer'

const SPOT = '<span class="search-landing"></span>'

describe('withSearchLandingPoint', () => {
  it('puts the landing point at the raw offset', () => {
    expect(withSearchLandingPoint('abc def', 4)).toBe(`abc ${SPOT}def`)
  })

  it('places it right before a tag that starts at the offset', () => {
    expect(withSearchLandingPoint('ab<b>c</b>', 2)).toBe(`ab${SPOT}<b>c</b>`)
  })

  it('counts tags as raw characters', () => {
    expect(withSearchLandingPoint('<b>x</b>yz', 8)).toBe(`<b>x</b>${SPOT}yz`)
  })

  it('leaves the line alone past its end', () => {
    expect(withSearchLandingPoint('abc', 3)).toBe('abc')
    expect(withSearchLandingPoint('abc', -1)).toBe('abc')
  })

  it('leaves the line alone inside a tag', () => {
    expect(withSearchLandingPoint('a<b class="x">c</b>', 4)).toBe('a<b class="x">c</b>')
  })

  it('leaves the line alone inside an entity', () => {
    expect(withSearchLandingPoint('a&nbsp;b', 3)).toBe('a&nbsp;b')
  })
})
