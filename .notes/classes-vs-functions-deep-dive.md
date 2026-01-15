# Classes vs Functions: Deep Dive with Reusability Patterns

**Critical Question**: When code is used in multiple places, how do we handle it with functions vs classes?

**Date**: January 15, 2026

**CRITICAL CONTEXT**: This document is about **HOW** to structure code (classes vs functions), not **WHAT** features to implement. All examples preserve the same functionality - the question is which implementation pattern is clearer and more maintainable.

---

## Part 1: The Core Reusability Problem

### The Scenario

You have logic that needs to be used in multiple places. How do you structure it?

**Example**: CSharpBridge is used by:
- dbManager
- pdfService  
- hebrewBooksService
- Multiple components

**Question**: Does this mean it SHOULD be a class?

**Answer**: No. Reusability doesn't require classes.

---

## Part 2: Reusability Patterns Compared

### Pattern 1: Class with Singleton (Current Approach)

```typescript
// csharpBridge.ts
let bridgeInstance: CSharpBridge | null = null

export class CSharpBridge {
    private pendingRequests = new Map()
    
    constructor() {
        if (!bridgeInstance) {
            this.setupGlobalHandlers()
            bridgeInstance = this
        } else {
            return bridgeInstance
        }
    }
    
    static getInstance(): CSharpBridge {
        if (!bridgeInstance) {
            bridgeInstance = new CSharpBridge()
        }
        return bridgeInstance
    }
    
    send(command: string, args: any[]) { /* ... */ }
}

// Usage in multiple places
// dbManager.ts
const bridge = CSharpBridge.getInstance()
bridge.send('GetTree', [])

// pdfService.ts
const bridge = CSharpBridge.getInstance()
bridge.send('OpenPdf', [path])

// hebrewBooksService.ts
const bridge = CSharpBridge.getInstance()
bridge.send('DownloadBook', [id])
```

**Issues**:
- Verbose getInstance() calls
- Singleton pattern complexity
- Hard to test (global state)
- Class overhead for no benefit

---

### Pattern 2: Module-Level Singleton (Simpler)

```typescript
// csharpBridge.ts
const pendingRequests = new Map()

function isAvailable(): boolean {
    return typeof window !== 'undefined' && 
           (window as any).chrome?.webview !== undefined
}

function send(command: string, args: any[]): void {
    if (!isAvailable()) return
    (window as any).chrome.webview.postMessage({ command, args })
}

function createRequest<T>(requestId: string): Promise<T> {
    return new Promise((resolve, reject) => {
        pendingRequests.set(requestId, { resolve, reject })
    })
}

// Setup once when module loads
setupGlobalHandlers()

function setupGlobalHandlers() {
    if (typeof window === 'undefined') return
    const win = window as any
    
    win.receiveTreeData = (data: any) => {
        const request = pendingRequests.get('GetTree')
        if (request) {
            request.resolve(data)
            pendingRequests.delete('GetTree')
        }
    }
    // ... other handlers
}

// Export simple object
export const bridge = {
    isAvailable,
    send,
    createRequest
}

// Usage in multiple places
// dbManager.ts
import { bridge } from './csharpBridge'
bridge.send('GetTree', [])

// pdfService.ts
import { bridge } from './csharpBridge'
bridge.send('OpenPdf', [path])

// hebrewBooksService.ts
import { bridge } from './csharpBridge'
bridge.send('DownloadBook', [id])
```

**Benefits**:
- Same shared state (module is singleton by nature)
- Simpler imports
- No getInstance() ceremony
- Easier to test (can mock the export)
- Less code

**Key Insight**: **JavaScript modules are already singletons!**

---

### Pattern 3: Factory Function (When You Need Multiple Instances)

**When you actually need different instances**:

```typescript
// logger.ts
export function createLogger(name: string) {
    const logs: string[] = []
    
    function log(message: string) {
        const entry = `[${name}] ${new Date().toISOString()}: ${message}`
        logs.push(entry)
        console.log(entry)
    }
    
    function getLogs() {
        return [...logs]
    }
    
    function clear() {
        logs.length = 0
    }
    
    return { log, getLogs, clear }
}

// Usage - different instances for different modules
// dbManager.ts
const logger = createLogger('Database')
logger.log('Fetching data')

// pdfService.ts
const logger = createLogger('PDF')
logger.log('Loading PDF')

// Each has its own state!
```

**When to use**:
- Need multiple independent instances
- Each instance has its own state
- State shouldn't be shared

**Class equivalent**:
```typescript
class Logger {
    private logs: string[] = []
    
    constructor(private name: string) {}
    
    log(message: string) {
        const entry = `[${this.name}] ${new Date().toISOString()}: ${message}`
        this.logs.push(entry)
        console.log(entry)
    }
    
    getLogs() {
        return [...this.logs]
    }
}

// Usage
const dbLogger = new Logger('Database')
const pdfLogger = new Logger('PDF')
```

**Both work equally well here** - this is a case where class is acceptable.

---

## Part 3: Shared State Patterns

### Scenario: Multiple Components Need Same Data

**Example**: Multiple components need access to book tree data.

### Anti-Pattern: Class with Shared State

```typescript
// ❌ BAD - Class with shared state
class TreeDataManager {
    private static instance: TreeDataManager
    private treeData: TreeNode[] = []
    
    private constructor() {}
    
    static getInstance() {
        if (!TreeDataManager.instance) {
            TreeDataManager.instance = new TreeDataManager()
        }
        return TreeDataManager.instance
    }
    
    async loadTree() {
        this.treeData = await fetchTree()
    }
    
    getTree() {
        return this.treeData
    }
}

// Usage in multiple components
// Component A
const manager = TreeDataManager.getInstance()
await manager.loadTree()

// Component B
const manager = TreeDataManager.getInstance()
const tree = manager.getTree()
```

**Issues**:
- Singleton ceremony
- Not reactive (Vue won't detect changes)
- Hard to test
- Verbose

---

### Better Pattern: Pinia Store (Vue)

```typescript
// stores/treeStore.ts
import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useTreeStore = defineStore('tree', () => {
    const treeData = ref<TreeNode[]>([])
    const isLoading = ref(false)
    
    async function loadTree() {
        isLoading.value = true
        try {
            treeData.value = await fetchTree()
        } finally {
            isLoading.value = false
        }
    }
    
    return { treeData, isLoading, loadTree }
})

// Usage in multiple components
// Component A
const treeStore = useTreeStore()
await treeStore.loadTree()

// Component B
const treeStore = useTreeStore()
const tree = treeStore.treeData // Reactive!
```

**Benefits**:
- Reactive (Vue detects changes)
- Shared state across components
- No singleton pattern
- Easy to test
- Framework-native

---

### Alternative: Composable with Shared State

```typescript
// composables/useTree.ts
import { ref } from 'vue'

// Shared state (outside composable)
const treeData = ref<TreeNode[]>([])
const isLoading = ref(false)

export function useTree() {
    async function loadTree() {
        if (treeData.value.length > 0) return // Already loaded
        
        isLoading.value = true
        try {
            treeData.value = await fetchTree()
        } finally {
            isLoading.value = false
        }
    }
    
    return {
        treeData,
        isLoading,
        loadTree
    }
}

// Usage in multiple components
// Component A
const { treeData, loadTree } = useTree()
await loadTree()

// Component B
const { treeData } = useTree()
// Same treeData reference!
```

**Benefits**:
- Shared state (defined outside composable)
- Reactive
- Simple
- No class needed

**Key Insight**: **State defined outside the composable is shared across all uses.**

---

## Part 4: The Multiple Instance Problem

### When You Actually Need Multiple Instances

**Example**: BookLineViewerState - each tab needs its own instance.

### Current Approach: Class

```typescript
// bookLineViewerState.ts
export class BookLineViewerState {
    lines = ref<Record<number, string>>({})
    totalLines = ref(0)
    
    async loadBook(bookId: number) { /* ... */ }
    async loadLinesAround(center: number) { /* ... */ }
}

// Usage - each component creates its own instance
// BookLineViewer.vue (Tab 1)
const viewerState = new BookLineViewerState()
await viewerState.loadBook(1)

// BookLineViewer.vue (Tab 2)
const viewerState = new BookLineViewerState()
await viewerState.loadBook(2)
```

**This is a valid use case for classes!** Each tab needs independent state.

---

### Alternative: Factory Function

```typescript
// bookLineViewer.ts
export function createBookLineViewer() {
    const lines = ref<Record<number, string>>({})
    const totalLines = ref(0)
    
    async function loadBook(bookId: number) {
        totalLines.value = await getTotalLines(bookId)
        // ... load lines
    }
    
    async function loadLinesAround(center: number) {
        // ... load lines around center
    }
    
    return {
        lines,
        totalLines,
        loadBook,
        loadLinesAround
    }
}

// Usage - each component creates its own instance
// BookLineViewer.vue (Tab 1)
const viewer = createBookLineViewer()
await viewer.loadBook(1)

// BookLineViewer.vue (Tab 2)
const viewer = createBookLineViewer()
await viewer.loadBook(2)
```

**Both approaches work equally well here.**

**Difference**:
- Class: `new BookLineViewerState()`
- Factory: `createBookLineViewer()`

**Choose based on**:
- Team preference
- Consistency with codebase
- Testing needs

---

## Part 5: Testing Implications

### Singleton Class (Hard to Test)

```typescript
// csharpBridge.ts
class CSharpBridge {
    private static instance: CSharpBridge
    static getInstance() { /* ... */ }
}

// Test
test('should send command', () => {
    const bridge = CSharpBridge.getInstance()
    // Problem: Can't reset state between tests!
    // Problem: Can't mock easily!
})
```

---

### Module Export (Easy to Test)

```typescript
// csharpBridge.ts
export const bridge = {
    send,
    createRequest
}

// Test
import * as bridgeModule from './csharpBridge'

test('should send command', () => {
    const mockSend = vi.fn()
    vi.spyOn(bridgeModule.bridge, 'send').mockImplementation(mockSend)
    
    bridge.send('Test', [])
    expect(mockSend).toHaveBeenCalled()
})
```

---

### Factory Function (Easy to Test)

```typescript
// logger.ts
export function createLogger(name: string) { /* ... */ }

// Test
test('should log messages', () => {
    const logger = createLogger('Test')
    logger.log('Hello')
    
    const logs = logger.getLogs()
    expect(logs).toContain('Hello')
    // Each test gets fresh instance!
})
```

---

## Part 6: Real-World Decision Matrix

### Scenario 1: Shared Utility (No State)

**Example**: String formatting functions

```typescript
// ✅ BEST: Plain functions
export function capitalize(str: string): string {
    return str.charAt(0).toUpperCase() + str.slice(1)
}

export function truncate(str: string, length: number): string {
    return str.length > length ? str.slice(0, length) + '...' : str
}

// Usage everywhere
import { capitalize, truncate } from './stringUtils'
```

**Why**: No state, pure functions, tree-shakeable

---

### Scenario 2: Shared State (Singleton)

**Example**: CSharpBridge - one connection to C#

```typescript
// ✅ BEST: Module-level singleton
const pendingRequests = new Map()

export const bridge = {
    send(command: string, args: any[]) { /* ... */ },
    createRequest<T>(id: string): Promise<T> { /* ... */ }
}

// Usage everywhere
import { bridge } from './csharpBridge'
```

**Why**: Module is already singleton, simpler than class

---

### Scenario 3: Multiple Independent Instances

**Example**: Logger - each module has its own

```typescript
// ✅ GOOD: Factory function OR class
export function createLogger(name: string) {
    const logs: string[] = []
    return { log, getLogs, clear }
}

// OR

export class Logger {
    private logs: string[] = []
    constructor(private name: string) {}
    log(message: string) { /* ... */ }
}

// Usage
const dbLogger = createLogger('DB')
const pdfLogger = createLogger('PDF')
```

**Why**: Need independent state per instance - both work

---

### Scenario 4: Reactive Shared State (Vue)

**Example**: Tree data shared across components

```typescript
// ✅ BEST: Pinia store
export const useTreeStore = defineStore('tree', () => {
    const treeData = ref<TreeNode[]>([])
    async function loadTree() { /* ... */ }
    return { treeData, loadTree }
})

// Usage in components
const treeStore = useTreeStore()
```

**Why**: Framework-native, reactive, shared state

---

### Scenario 5: Complex Stateful Object

**Example**: WebSocket connection with lifecycle

```typescript
// ✅ GOOD: Class is appropriate here
export class WebSocketConnection {
    private socket: WebSocket | null = null
    private reconnectTimer: number | null = null
    
    async connect() { /* ... */ }
    send(message: string) { /* ... */ }
    disconnect() { /* ... */ }
}

// Usage
const ws = new WebSocketConnection('ws://...')
await ws.connect()
```

**Why**: Complex lifecycle, manages resources, encapsulation

---

## Part 7: Your Codebase Analysis

### CSharpBridge

**Current**: Class with singleton
**Used by**: dbManager, pdfService, hebrewBooksService, components
**Needs**: Shared state (one connection to C#)

**Recommendation**: Module-level singleton

```typescript
// ✅ Convert to module singleton
export const bridge = {
    send,
    createRequest,
    isAvailable
}
```

**Why**: Simpler, same functionality, easier to use

---

### DbManager

**Current**: Class wrapper
**Used by**: All data-fetching code
**Needs**: Routing logic (no state)

**Recommendation**: Plain functions

```typescript
// ✅ Convert to functions
export async function getTree() { /* ... */ }
export async function getToc(bookId: number) { /* ... */ }
```

**Why**: No state, just routing, simpler

---

### BookLineViewerState

**Current**: Class
**Used by**: Each BookLineViewer component (multiple instances)
**Needs**: Independent state per tab

**Recommendation**: Factory function OR keep class (both work)

```typescript
// ✅ Option A: Factory
export function createBookLineViewer() {
    const lines = ref({})
    return { lines, loadBook, loadLinesAround }
}

// ✅ Option B: Keep class (also fine)
export class BookLineViewerState {
    lines = ref({})
    async loadBook() { /* ... */ }
}
```

**Why**: Need multiple instances - both patterns work

**Preference**: Factory function (matches Vue style better)

---

### CommentaryManager

**Current**: Class with singleton
**Used by**: BookCommentaryView component
**Needs**: Just one function

**Recommendation**: Plain function

```typescript
// ✅ Convert to function
export async function loadCommentaryLinks(
    bookId: number,
    lineIndex: number,
    tabId: string
): Promise<CommentaryLinkGroup[]> {
    // ... implementation
}
```

**Why**: No state, single function, simpler

---

## Part 8: Migration Strategy

### Step 1: Identify Pattern

For each class, ask:

1. **Is it a singleton?**
   - YES → Module-level export
   - NO → Continue

2. **Does it have state?**
   - NO → Plain functions
   - YES → Continue

3. **Need multiple instances?**
   - YES → Factory function or keep class
   - NO → Module-level singleton

4. **Is it reactive state (Vue)?**
   - YES → Pinia store or composable
   - NO → Continue

---

### Step 2: Convert Safely

**For Singleton Classes**:

```typescript
// Before
class MyService {
    private static instance: MyService
    static getInstance() { /* ... */ }
    doThing() { /* ... */ }
}

// After
const state = { /* ... */ }

function doThing() { /* ... */ }

export const myService = {
    doThing
}

// Update all imports
// Before: MyService.getInstance().doThing()
// After: myService.doThing()
```

---

### Step 3: Test Thoroughly

After each conversion:
1. Run all tests
2. Test in browser
3. Check all usage sites
4. Commit

**Don't convert everything at once!**

---

## Part 9: Key Principles

### Principle 1: Modules Are Singletons

**You don't need singleton classes in JavaScript.**

```typescript
// This is already a singleton!
const sharedState = { /* ... */ }

export function doThing() {
    // Uses sharedState
}

// Every import gets the same sharedState
```

---

### Principle 2: Shared State ≠ Class

**Shared state doesn't require classes.**

```typescript
// Shared state with functions
const cache = new Map()

export function get(key: string) {
    return cache.get(key)
}

export function set(key: string, value: any) {
    cache.set(key, value)
}

// All imports share the same cache
```

---

### Principle 3: Multiple Instances = Factory or Class

**When you need multiple independent instances:**

```typescript
// Factory function
export function createThing() {
    const state = {}
    return { doStuff }
}

// OR Class
export class Thing {
    private state = {}
    doStuff() { /* ... */ }
}

// Both work - choose based on preference
```

---

### Principle 4: Reactive State = Store/Composable

**In Vue, use framework patterns:**

```typescript
// Pinia store for shared reactive state
export const useMyStore = defineStore('my', () => {
    const data = ref([])
    return { data }
})

// Composable for reusable logic
export function useMyFeature() {
    const state = ref()
    return { state }
}
```

---

## Part 10: Common Misconceptions

### Misconception 1: "Reusability Requires Classes"

**FALSE**

```typescript
// Reusable without class
export function formatDate(date: Date): string {
    return date.toISOString()
}

// Used everywhere
import { formatDate } from './utils'
```

---

### Misconception 2: "Shared State Requires Classes"

**FALSE**

```typescript
// Shared state without class
const users = new Map()

export function getUser(id: number) {
    return users.get(id)
}

export function setUser(id: number, user: User) {
    users.set(id, user)
}

// All imports share the same users Map
```

---

### Misconception 3: "Multiple Instances Require Classes"

**FALSE**

```typescript
// Multiple instances without class
export function createCounter() {
    let count = 0
    return {
        increment: () => count++,
        getCount: () => count
    }
}

const counter1 = createCounter()
const counter2 = createCounter()
// Independent instances!
```

---

## Conclusion

### The Answer to Your Question

**"When code is used in multiple places, how do we handle it?"**

**Answer**: It depends on what you're sharing:

1. **Sharing functions** → Export functions
2. **Sharing state (singleton)** → Module-level export
3. **Sharing state (reactive)** → Pinia store or composable
4. **Need multiple instances** → Factory function or class (both work)

**Classes are NOT required for reusability.**

### For Your Codebase

- **CSharpBridge**: Module singleton (not class) - **Keep all bridge functionality, change implementation pattern**
- **DbManager**: Plain functions (not class) - **Keep all routing logic, change implementation pattern**
- **BookLineViewerState**: Factory function or composable - **Keep virtualization, dual-mode, all features, change implementation pattern**
- **CommentaryManager**: Plain function (not class) - **Keep commentary loading, change implementation pattern**

**3 out of 5 should definitely not be classes.**
**1 out of 5 could go either way (BookLineViewerState).**

**Critical Point**: Converting from classes to functions means changing HOW the code is structured, not WHAT it does. All features, all functionality, all behavior stays the same - just implemented with clearer patterns.

### Final Rule

**Use classes when they provide clear value over simpler alternatives.**

**Reusability is NOT a reason to use classes.**

**Choosing functions over classes is about implementation clarity, not feature removal.**
