export function cumulativeOffsetsFromHeights(heights: Record<number, number>, count: number, estimate: number): number[] {
    const offsets: number[] = []
    let acc = 0
    for (let i = 0; i < count; i++) {
        offsets.push(acc)
        acc += heights[i] ?? estimate
    }
    return offsets
}

export function findIndexForScroll(offsets: number[], top: number): number {
    let low = 0
    let high = offsets.length - 1
    let mid
    while (low <= high) {
        mid = Math.floor((low + high) / 2)
        const off = offsets[mid] ?? 0
        if (off <= top) {
            low = mid + 1
        } else {
            high = mid - 1
        }
    }
    return Math.max(0, low - 1)
}
