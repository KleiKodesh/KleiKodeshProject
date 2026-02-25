<template>
    <div class="app">
        <h1>Vite scrollIntoView Issue Reproduction</h1>
        <p class="instructions">
            Click any item in the navigation to scroll to it. In dev mode, the scroll position will be incorrect.
            Build for production and the issue disappears.
        </p>

        <div class="layout">
            <!-- Navigation sidebar -->
            <div class="sidebar">
                <h2>Navigation</h2>
                <div class="nav-list">
                    <button v-for="item in items"
                            :key="item.id"
                            @click="scrollToItem(item.id)"
                            class="nav-button"
                            :class="{ active: currentItem === item.id }">
                        {{ item.title }}
                    </button>
                </div>
            </div>

            <!-- Scrollable content -->
            <div class="content"
                 ref="contentRef">
                <div v-for="item in items"
                     :key="item.id"
                     :data-id="item.id"
                     class="content-item"
                     :class="{ highlighted: currentItem === item.id }">
                    <h3>{{ item.title }}</h3>
                    <p>{{ item.content }}</p>
                </div>
            </div>
        </div>

        <div class="debug-info">
            <h3>Debug Info</h3>
            <p>Current Item: {{ currentItem }}</p>
            <p>Mode: {{ mode }}</p>
            <p>Dev: {{ isDev }}</p>
            <p>Prod: {{ isProd }}</p>
        </div>
    </div>
</template>

<script setup>
import { ref, nextTick } from 'vue'

const contentRef = ref(null)
const currentItem = ref(null)

// Environment info
const mode = import.meta.env.MODE
const isDev = import.meta.env.DEV
const isProd = import.meta.env.PROD

// Generate 100 items to ensure scrolling is needed
const items = Array.from({ length: 100 }, (_, i) => ({
    id: i,
    title: `Section ${i}`,
    content: `This is the content for section ${i}. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.`
}))

function scrollToItem(itemId) {
    currentItem.value = itemId

    nextTick(() => {
        const container = contentRef.value
        if (!container) return

        const element = container.querySelector(`[data-id="${itemId}"]`)
        if (element) {
            console.log(`Scrolling to item ${itemId}`)

            // This should scroll the element to the center of the viewport
            // In Vite dev mode, it jumps to the wrong position
            element.scrollIntoView({ behavior: 'auto', block: 'center' })

            // Log the position after scroll
            setTimeout(() => {
                const rect = element.getBoundingClientRect()
                const containerRect = container.getBoundingClientRect()
                console.log('Element position after scroll:', {
                    elementTop: rect.top,
                    containerTop: containerRect.top,
                    offset: rect.top - containerRect.top,
                    expectedOffset: 'should be near center of container'
                })
            }, 100)
        }
    })
}
</script>

<style scoped>
.app {
    background: white;
    border-radius: 8px;
    padding: 20px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
}

h1 {
    margin-bottom: 10px;
    color: #333;
}

.instructions {
    margin-bottom: 20px;
    padding: 15px;
    background: #fff3cd;
    border: 1px solid #ffc107;
    border-radius: 4px;
    color: #856404;
}

.layout {
    display: flex;
    gap: 20px;
    margin-bottom: 20px;
}

.sidebar {
    width: 250px;
    flex-shrink: 0;
}

.sidebar h2 {
    font-size: 18px;
    margin-bottom: 10px;
    color: #555;
}

.nav-list {
    display: flex;
    flex-direction: column;
    gap: 5px;
    max-height: 600px;
    overflow-y: auto;
    border: 1px solid #ddd;
    border-radius: 4px;
    padding: 10px;
    background: #fafafa;
}

.nav-button {
    padding: 10px 15px;
    border: 1px solid #ddd;
    background: white;
    border-radius: 4px;
    cursor: pointer;
    text-align: left;
    transition: all 0.2s;
}

.nav-button:hover {
    background: #f0f0f0;
    border-color: #999;
}

.nav-button.active {
    background: #007bff;
    color: white;
    border-color: #007bff;
}

.content {
    flex: 1;
    height: 600px;
    overflow-y: auto;
    border: 2px solid #007bff;
    border-radius: 4px;
    padding: 20px;
    background: #fafafa;
}

.content-item {
    margin-bottom: 30px;
    padding: 20px;
    background: white;
    border: 1px solid #ddd;
    border-radius: 4px;
    transition: all 0.3s;
}

.content-item.highlighted {
    border-color: #007bff;
    box-shadow: 0 0 0 3px rgba(0, 123, 255, 0.1);
    background: #f0f8ff;
}

.content-item h3 {
    margin-bottom: 10px;
    color: #333;
}

.content-item p {
    color: #666;
    line-height: 1.6;
}

.debug-info {
    margin-top: 20px;
    padding: 15px;
    background: #e9ecef;
    border-radius: 4px;
    font-family: monospace;
    font-size: 14px;
}

.debug-info h3 {
    margin-bottom: 10px;
    font-size: 16px;
}

.debug-info p {
    margin: 5px 0;
}
</style>
