import { afterEach, describe, expect, it, vi } from 'vitest'

// The hosted app: serviceGetUrl answers null (the relay serves POST only), so pictures come
// through the fastDocViewPicture RPC as object URLs.
const serviceCall = vi.fn()
vi.mock('./serviceClient', () => ({
  serviceCall: (...args: unknown[]) => serviceCall(...args),
  serviceGetUrl: async () => null,
}))

const { fastDocViewApi } = await import('./fastDocViewApi')

describe('fastDocViewApi.pictureSource (hosted)', () => {
  afterEach(() => { serviceCall.mockReset(); vi.unstubAllGlobals() })

  it('reads the bytes through the RPC op and returns an object URL of them', async () => {
    const created: Blob[] = []
    vi.stubGlobal('URL', { createObjectURL: (blob: Blob) => { created.push(blob); return 'blob:x/1' }, revokeObjectURL: () => {} })
    serviceCall.mockResolvedValue({ contentType: 'image/png', bytes: new Uint8Array([137, 80, 78, 71]) })
    expect(await fastDocViewApi.pictureSource('doc', 'p:1')).toBe('blob:x/1')
    expect(serviceCall).toHaveBeenCalledWith('fastDocViewPicture', { documentId: 'doc', pictureId: 'p:1' })
    expect(created[0]?.type).toBe('image/png')
    expect(created[0]?.size).toBe(4)
  })

  it('answers null, without an object URL, for a missing picture or a closed document', async () => {
    const createObjectURL = vi.fn()
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL: () => {} })
    serviceCall.mockResolvedValue(undefined)                       // the service's empty result
    expect(await fastDocViewApi.pictureSource('doc', 'gone')).toBeNull()
    serviceCall.mockResolvedValue({ contentType: 'image/png', bytes: new Uint8Array(0) })
    expect(await fastDocViewApi.pictureSource('doc', 'empty')).toBeNull()
    expect(createObjectURL).not.toHaveBeenCalled()
  })
})
