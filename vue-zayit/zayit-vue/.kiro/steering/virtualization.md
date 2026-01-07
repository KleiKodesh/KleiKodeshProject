# BookLineViewer Virtualization Architecture

## Overview

Dual-mode system with different source of truth and optimized batching for efficient database access.

## Architecture Modes

### **Virtualization ON (Streaming Mode)**
- **Source of Truth**: Database
- **Buffer**: Cleared/unused
- **Loading**: Batched range loading for visible content only
- **Search**: Direct DB queries with SQL LIKE
- **Memory**: Minimal (only visible + buffer zone)
- **Efficiency**: Intelligent request batching, parallel loading

### **Virtualization OFF (Buffer Mode)**
- **Source of Truth**: In-memory buffer
- **Buffer**: Progressive loading fills buffer completely
- **Loading**: All content eventually loaded via background process
- **Search**: Buffer-based JavaScript search
- **Memory**: Full document in memory
- **Efficiency**: Fast access after initial loading

## Components

### **BookLineViewerState**
- `lines.value` - Currently visible lines (UI layer)
- `lineBuffer` - Used only when virtualization OFF
- `virtualizationEnabled` - Controls which mode to use

## Flow

### **Virtualization ON (DB Source of Truth)**
```
Book loads → No background loading → Load only visible content
Lines visible → Batch load ranges from DB to UI (efficient)
Search → Query DB directly
Memory cleanup → Remove non-visible from UI
```

### **Virtualization OFF (Buffer Source of Truth)**
```
Book loads → Start progressive loading → Fill buffer
Line visible → Copy from buffer to UI (if available)
Search → Search buffer content
No cleanup → Keep all content in memory
```

## Loading Strategy

### **Virtualization ON (Optimized Batching)**
- **No background loading** - Only load what's needed when needed
- **Intersection observer batching** - Collects visible lines over 50ms window
- **Range consolidation** - Identifies continuous missing ranges
- **Parallel loading** - Multiple non-adjacent ranges loaded simultaneously
- **Smart gap detection** - Skips already-loaded content
- **Efficient SQL queries** - Single range requests instead of individual lines
- **Memory efficient** - Only visible + buffer zone in memory

### **Virtualization OFF (Progressive Buffer)**
- **Background progressive loading** - Fills buffer gradually in background
- **Buffer-first strategy** - Check buffer before hitting DB
- **Complete document loading** - Eventually loads entire document
- **Reactive search updates** - Re-searches as buffer fills
- **Memory intensive** - Full document kept in memory

## Search Integration

### **Virtualization ON**
- **Source**: Database queries (`searchLines`)
- **Method**: SQL LIKE queries
- **Scope**: Entire document (DB handles)
- **Performance**: DB-optimized

### **Virtualization OFF**
- **Source**: In-memory buffer
- **Method**: JavaScript string search
- **Scope**: Loaded content only (progressive)
- **Performance**: Memory-based, reactive updates

## Batching Optimizations (Virtualization ON)

### **Request Batching**
- **Debounced collection**: 50ms window to collect newly visible lines
- **Range calculation**: Finds min/max of visible lines + buffer zone
- **Gap analysis**: Identifies continuous ranges that need loading
- **Parallel execution**: Multiple ranges loaded simultaneously
- **No redundancy**: Skips already-loaded content

### **Intersection Observer Efficiency**
```javascript
// Instead of: N individual loadLinesAround() calls
entries.forEach(entry => loadLinesAround(lineIndex))

// Now: Single batched call for all visible lines
batchLoadVisibleLines([...newlyVisibleLines])
```

### **Database Query Optimization**
```sql
-- Instead of: Multiple single-line queries
SELECT content FROM line WHERE bookId = ? AND lineIndex = 1
SELECT content FROM line WHERE bookId = ? AND lineIndex = 2
SELECT content FROM line WHERE bookId = ? AND lineIndex = 3

-- Now: Single range query
SELECT lineIndex, content FROM line 
WHERE bookId = ? AND lineIndex BETWEEN 1 AND 50
ORDER BY lineIndex
```

## Key Rules

1. **Virtualization setting controls source of truth and loading strategy**
2. **ON = DB source with batched loading, OFF = Buffer source with progressive loading**
3. **Buffer cleared when switching to virtualization ON**
4. **Search method and efficiency changes based on mode**
5. **Memory usage dramatically different between modes**
6. **Batching minimizes DB requests in virtualization mode**
7. **No progressive loading when virtualization ON - only on-demand batched loading**

## Performance Characteristics

### **Virtualization ON (Optimized)**
- **Memory**: ~50-500 lines in memory (configurable buffer)
- **Search**: Instant DB queries with SQL LIKE, complete document scope
- **Loading**: Highly efficient batched ranges, parallel execution
- **Startup**: Instant (no background processes)
- **DB Requests**: Minimized (1-3 range queries vs dozens of individual queries)
- **Scrolling**: Smooth with predictive loading (200px rootMargin)
- **Network**: Optimal - only loads what's visible + small buffer

### **Virtualization OFF (Traditional)**
- **Memory**: Full document in memory (can be 100MB+ for large texts)
- **Search**: Fast in-memory JavaScript search (after initial loading)
- **Loading**: Progressive background loading over time
- **Startup**: Slower initial load, but faster subsequent operations
- **DB Requests**: Many small requests during background loading
- **Scrolling**: Instant (all content pre-loaded)
- **Network**: Heavy - loads entire document eventually

## Implementation Details

### **BookLineViewerState Methods**
- `setVirtualizationMode(enabled)` - Switches modes, clears buffer if enabling
- `loadRangeEfficiently(start, end)` - Batches continuous ranges, parallel loading
- `searchInDB(term)` - Direct database search for virtualization mode
- `getSearchData()` - Returns appropriate search source based on mode

### **BookLineViewer Optimizations**
- Debounced intersection observer (50ms batching window)
- Smart cleanup timeout management
- Efficient range calculation for batch loading
- Parallel promise execution for multiple ranges