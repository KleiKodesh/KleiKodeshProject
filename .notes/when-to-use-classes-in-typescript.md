# When to Use Classes in TypeScript

**The Nuanced Answer**: Classes aren't evil - **unnecessary classes** are evil.

**Date**: January 15, 2026

**CRITICAL CONTEXT**: This is about **HOW** to implement (classes vs functions), not **WHAT** to implement (features). The goal is to choose the right implementation pattern for each situation while preserving all necessary functionality.

---

## The Core Principle

**Use classes when they provide clear value. Use functions when they're simpler.**

Classes are a tool. Like any tool, they have appropriate and inappropriate uses.

**Important**: Choosing functions over classes doesn't mean removing features. It means implementing the same features with simpler, more maintainable patterns.

---

## Part 1: When Classes ARE Appropriate

### 1. True Object-Oriented Patterns

**When you need inheritance and polymorphism**:

```typescript
// ✅ GOOD - Classes make sense here
abstract class Shape {
    abstract area(): number
    abstract perimeter(): number
}

class Circle extends Shape {
    constructor(private radius: number) {
        super()
    }
    
    area(): number {
        return Math.PI * this.radius ** 2
    }
    
    perimeter(): number {
        return 2 * Math.PI * this.radius
    }
}

class Rectangle extends Shape {
    constructor(private width: number, private height: number) {
        super()
    }
    
    area(): number {
        return this.width * this.height
    }
    
    perimeter(): number {
        return 2 * (this.width + this.height)
    }
}

// Usage
function calculateTotalArea(shapes: Shape[]): number {
    return shapes.reduce((sum, shape) => sum + shape.area(), 0)
}
```

**Why this works**:
- True polymorphism (different shapes, same interface)
- Inheritance provides shared behavior
- Type system enforces contracts
- Can't easily do this with functions

---

### 2. Stateful Objects with Lifecycle

**When you need initialization, state, and cleanup**:

```typescript
// ✅ GOOD - Class manages complex lifecycle
class WebSocketConnection {
    private socket: WebSocket | null = null
    private reconnectTimer: number | null = null
    private messageQueue: string[] = []
    
    constructor(private url: string) {}
    
    async connect(): Promise<void> {
        this.socket = new WebSocket(this.url)
        
        this.socket.onopen = () => {
            this.flushMessageQueue()
        }
        
        this.socket.onclose = () => {
            this.scheduleReconnect()
        }
    }
    
    send(message: string): void {
        if (this.socket?.readyState === WebSocket.OPEN) {
            this.socket.send(message)
        } else {
            this.messageQueue.push(message)
        }
    }
    
    private flushMessageQueue(): void {
        while (this.messageQueue.length > 0) {
            const message = this.messageQueue.shift()!
            this.socket?.send(message)
        }
    }
    
    private scheduleReconnect(): void {
        this.reconnectTimer = window.setTimeout(() => {
            this.connect()
        }, 5000)
    }
    
    disconnect(): void {
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer)
        }
        this.socket?.close()
        this.socket = null
    }
}
```

**Why this works**:
- Complex internal state (socket, timer, queue)
- Lifecycle methods (connect, disconnect)
- Private methods for encapsulation
- Clear initialization pattern

**Function alternative would be messy**:
```typescript
// ❌ BAD - Functions get messy here
function createWebSocketConnection(url: string) {
    let socket: WebSocket | null = null
    let reconnectTimer: number | null = null
    let messageQueue: string[] = []
    
    function connect() { /* ... */ }
    function send(message: string) { /* ... */ }
    function flushMessageQueue() { /* ... */ }
    function scheduleReconnect() { /* ... */ }
    function disconnect() { /* ... */ }
    
    return { connect, send, disconnect }
}
```

**Why function version is worse**:
- All state in closure (harder to inspect)
- No clear initialization
- Private functions not truly private
- Harder to test individual methods

---

### 3. Framework/Library Requirements

**When the framework expects classes**:

```typescript
// ✅ GOOD - Angular requires classes
@Component({
    selector: 'app-user-profile',
    template: '<div>{{ user.name }}</div>'
})
export class UserProfileComponent implements OnInit {
    user: User | null = null
    
    ngOnInit() {
        this.loadUser()
    }
    
    async loadUser() {
        this.user = await this.userService.getUser()
    }
}
```

**Why this works**:
- Framework requirement (Angular)
- Decorators need classes
- Lifecycle hooks are class methods
- No choice in the matter

**Note**: Vue 3 Composition API doesn't require classes - that's why we prefer functions there.

---

### 4. Data Models with Behavior

**When data needs methods that operate on itself**:

```typescript
// ✅ GOOD - Class combines data + behavior
class Money {
    constructor(
        private amount: number,
        private currency: string
    ) {}
    
    add(other: Money): Money {
        if (this.currency !== other.currency) {
            throw new Error('Cannot add different currencies')
        }
        return new Money(this.amount + other.amount, this.currency)
    }
    
    multiply(factor: number): Money {
        return new Money(this.amount * factor, this.currency)
    }
    
    format(): string {
        return `${this.currency} ${this.amount.toFixed(2)}`
    }
    
    equals(other: Money): boolean {
        return this.amount === other.amount && 
               this.currency === other.currency
    }
}

// Usage
const price = new Money(100, 'USD')
const tax = price.multiply(0.1)
const total = price.add(tax)
console.log(total.format()) // "USD 110.00"
```

**Why this works**:
- Data and operations are tightly coupled
- Methods naturally belong to the data
- Immutable operations (returns new instances)
- Type-safe operations

**Function alternative is awkward**:
```typescript
// ⚠️ AWKWARD - Functions feel disconnected
interface Money {
    amount: number
    currency: string
}

function addMoney(a: Money, b: Money): Money {
    if (a.currency !== b.currency) {
        throw new Error('Cannot add different currencies')
    }
    return { amount: a.amount + b.amount, currency: a.currency }
}

function multiplyMoney(money: Money, factor: number): Money {
    return { amount: money.amount * factor, currency: money.currency }
}

// Usage feels disconnected
const price = { amount: 100, currency: 'USD' }
const tax = multiplyMoney(price, 0.1)
const total = addMoney(price, tax)
```

---

## Part 2: When Classes Are NOT Appropriate

### 1. Simple Data Containers

**When you just need to hold data**:

```typescript
// ❌ BAD - Unnecessary class
class User {
    constructor(
        public id: number,
        public name: string,
        public email: string
    ) {}
}

// ✅ GOOD - Simple interface/type
interface User {
    id: number
    name: string
    email: string
}

// Or even simpler
type User = {
    id: number
    name: string
    email: string
}
```

**Why interface is better**:
- No runtime overhead
- Simpler to read
- No instantiation needed
- TypeScript-only (compiles away)

---

### 2. Stateless Utilities

**When you just need functions**:

```typescript
// ❌ BAD - Unnecessary class
class StringUtils {
    static capitalize(str: string): string {
        return str.charAt(0).toUpperCase() + str.slice(1)
    }
    
    static truncate(str: string, length: number): string {
        return str.length > length ? str.slice(0, length) + '...' : str
    }
}

// Usage
StringUtils.capitalize('hello')

// ✅ GOOD - Just functions
export function capitalize(str: string): string {
    return str.charAt(0).toUpperCase() + str.slice(1)
}

export function truncate(str: string, length: number): string {
    return str.length > length ? str.slice(0, length) + '...' : str
}

// Usage
capitalize('hello')
```

**Why functions are better**:
- No class overhead
- Direct imports
- Tree-shakeable
- Simpler to use

---

### 3. Singletons (Usually)

**When you only need one instance**:

```typescript
// ❌ BAD - Singleton class
class ConfigManager {
    private static instance: ConfigManager
    private config: Config
    
    private constructor() {
        this.config = loadConfig()
    }
    
    static getInstance(): ConfigManager {
        if (!ConfigManager.instance) {
            ConfigManager.instance = new ConfigManager()
        }
        return ConfigManager.instance
    }
    
    get(key: string): any {
        return this.config[key]
    }
}

// Usage
ConfigManager.getInstance().get('apiUrl')

// ✅ GOOD - Module-level state
const config = loadConfig()

export function getConfig(key: string): any {
    return config[key]
}

// Usage
getConfig('apiUrl')
```

**Why module-level is better**:
- Simpler
- No getInstance() ceremony
- Modules are already singletons
- Less code

**Exception**: When you need lazy initialization or dependency injection.

---

### 4. Simple State Management

**When you just need reactive state**:

```typescript
// ❌ BAD - Unnecessary class
class CounterStore {
    private count = 0
    
    increment(): void {
        this.count++
    }
    
    decrement(): void {
        this.count--
    }
    
    getCount(): number {
        return this.count
    }
}

// ✅ GOOD - Composable/hook pattern
import { ref } from 'vue'

export function useCounter() {
    const count = ref(0)
    
    function increment() {
        count.value++
    }
    
    function decrement() {
        count.value--
    }
    
    return { count, increment, decrement }
}
```

**Why composable is better**:
- Framework-native reactivity
- Simpler to use
- No class overhead
- Matches Vue patterns

---

## Part 3: The Decision Tree

### Ask These Questions

**1. Do I need inheritance/polymorphism?**
- YES → Consider class
- NO → Continue

**2. Do I have complex internal state with lifecycle?**
- YES → Consider class
- NO → Continue

**3. Does the framework require it?**
- YES → Use class
- NO → Continue

**4. Is this data with tightly-coupled behavior?**
- YES → Consider class
- NO → Continue

**5. Is this just functions or simple data?**
- YES → Use functions/interfaces
- NO → Re-evaluate

---

## Part 4: Your Codebase Analysis

### Classes That Make Sense

**None in your current codebase!**

Let's check each one:

#### CSharpBridge
- ❌ No inheritance
- ❌ No complex lifecycle (just setup once)
- ❌ Not framework-required
- ❌ Not data with behavior
- **Verdict**: Should be functions

#### DatabaseManager
- ❌ No inheritance
- ❌ No state (just routing)
- ❌ Not framework-required
- ❌ Not data with behavior
- **Verdict**: Should be functions

#### BookLinesLoader
- ❌ No inheritance
- ❌ No state
- ❌ Not framework-required
- ❌ Just wraps other functions
- **Verdict**: Should be functions (or removed)

#### BookLineViewerState
- ❌ No inheritance
- ⚠️ Has complex state BUT...
- ❌ Not framework-required
- ❌ Could be composable instead
- **Verdict**: Should be composable (Vue pattern)

#### CommentaryManager
- ❌ No inheritance
- ❌ No state
- ❌ Not framework-required
- ❌ Just one function
- **Verdict**: Should be function

**Conclusion**: None of your classes meet the criteria for "appropriate class use".

**Important Note**: This doesn't mean the features should be removed! It means:
- CSharpBridge functionality → Keep all features, implement with functions
- DatabaseManager routing → Keep all routing logic, implement with functions  
- BookLineViewerState → Keep virtualization, dual-mode, all features, implement with composables/functions
- CommentaryManager → Keep commentary loading, implement with functions

Same features. Better implementation patterns.

---

## Part 5: When You SHOULD Use Classes

### Example 1: Virtual Scroller (Appropriate)

```typescript
// ✅ GOOD - Complex lifecycle and state
class VirtualScroller {
    private items: any[] = []
    private visibleRange = { start: 0, end: 0 }
    private scrollTop = 0
    private observer: IntersectionObserver | null = null
    
    constructor(
        private container: HTMLElement,
        private itemHeight: number
    ) {
        this.setupObserver()
    }
    
    setItems(items: any[]): void {
        this.items = items
        this.updateVisibleRange()
    }
    
    private setupObserver(): void {
        this.observer = new IntersectionObserver(
            entries => this.handleIntersection(entries),
            { root: this.container }
        )
    }
    
    private handleIntersection(entries: IntersectionObserverEntry[]): void {
        // Complex logic
    }
    
    private updateVisibleRange(): void {
        // Complex logic
    }
    
    destroy(): void {
        this.observer?.disconnect()
        this.observer = null
    }
}
```

**Why this is appropriate**:
- Complex internal state (items, range, observer)
- Lifecycle (constructor, destroy)
- Private methods for encapsulation
- Manages external resources (observer)

---

### Example 2: Form Validator (Appropriate)

```typescript
// ✅ GOOD - Stateful with clear lifecycle
class FormValidator {
    private errors = new Map<string, string[]>()
    private rules = new Map<string, ValidationRule[]>()
    
    addRule(field: string, rule: ValidationRule): void {
        if (!this.rules.has(field)) {
            this.rules.set(field, [])
        }
        this.rules.get(field)!.push(rule)
    }
    
    async validate(data: Record<string, any>): Promise<boolean> {
        this.errors.clear()
        
        for (const [field, rules] of this.rules) {
            for (const rule of rules) {
                const error = await rule.validate(data[field])
                if (error) {
                    this.addError(field, error)
                }
            }
        }
        
        return this.errors.size === 0
    }
    
    getErrors(field?: string): string[] {
        if (field) {
            return this.errors.get(field) || []
        }
        return Array.from(this.errors.values()).flat()
    }
    
    private addError(field: string, error: string): void {
        if (!this.errors.has(field)) {
            this.errors.set(field, [])
        }
        this.errors.get(field)!.push(error)
    }
    
    reset(): void {
        this.errors.clear()
    }
}
```

**Why this is appropriate**:
- Manages complex state (errors, rules)
- Multiple related operations
- Clear lifecycle (add rules, validate, reset)
- Encapsulation of error management

---

## Part 6: The Pragmatic Balance

### Your Preference (Updated)

**Default to functions, use classes when they provide clear value.**

**Clear value means**:
1. Inheritance/polymorphism is actually needed
2. Complex lifecycle with initialization/cleanup
3. Framework requires it
4. Data + behavior are inseparable

**If none of these apply → use functions.**

---

### Red Flags for Unnecessary Classes

🚩 All methods are static
🚩 No instance state
🚩 Only one instance ever created (singleton)
🚩 Just wrapping other functions
🚩 Could be a simple interface + functions
🚩 No private methods/state
🚩 No lifecycle (constructor/destructor)

---

### Green Flags for Appropriate Classes

✅ Needs inheritance hierarchy
✅ Complex internal state
✅ Lifecycle with setup/teardown
✅ Manages external resources
✅ Framework requirement
✅ Data + operations are inseparable

---

## Part 7: Refactoring Guide

### When You See a Class, Ask:

**1. Does it have instance state?**
- NO → Convert to functions
- YES → Continue

**2. Does it have private methods?**
- NO → Convert to functions
- YES → Continue

**3. Does it have lifecycle (constructor/destructor)?**
- NO → Convert to functions
- YES → Keep class (probably)

**4. Is it a singleton?**
- YES → Convert to module-level functions
- NO → Keep class (probably)

**5. Could this be a composable/hook?**
- YES (in Vue/React) → Convert to composable
- NO → Keep class

---

## Conclusion

### The Balance

**Classes are not evil. Unnecessary classes are evil.**

**Use classes when**:
- True OOP patterns (inheritance, polymorphism)
- Complex stateful objects with lifecycle
- Framework requirements
- Data models with inseparable behavior

**Use functions when**:
- Simple utilities
- Stateless operations
- Data containers
- Singletons (usually)
- Vue composables

### For Your Codebase

**All 5 classes should be functions/composables** because none meet the criteria for appropriate class use.

**This doesn't mean classes are always wrong** - it means these specific classes don't provide value over simpler alternatives.

---

**Rule of Thumb**: If you can't explain why a class is better than functions in one sentence, use functions.
