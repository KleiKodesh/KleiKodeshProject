<template>
    <svg class="circular-progress"
         viewBox="0 0 36 36">
        <path class="circle-bg"
              d="M18 2.0845
                 a 15.9155 15.9155 0 0 1 0 31.831
                 a 15.9155 15.9155 0 0 1 0 -31.831" />
        <path class="circle-progress"
              :stroke-dasharray="`${percentage}, 100`"
              d="M18 2.0845
                 a 15.9155 15.9155 0 0 1 0 31.831
                 a 15.9155 15.9155 0 0 1 0 -31.831" />
    </svg>
</template>

<script setup lang="ts">
defineProps<{
    percentage: number;
}>();
</script>

<style scoped>
.circular-progress {
    width: 1em;
    height: 1em;
}

.circle-bg {
    fill: none;
    stroke: var(--color-border);
    stroke-width: 3;
}

.circle-progress {
    fill: none;
    stroke: currentColor;
    stroke-width: 3;
    stroke-linecap: round;
    transform: rotate(-90deg);
    transform-origin: 50% 50%;
    transition: stroke-dasharray 0.3s ease;
}
</style>
