<template>
  <div class="flex-column height-fill">
    <div class="header-container">
      <TabHeader @click="toggleDropdown"
                 @close-tab-dropdown="closeDropdown"
                 :is-dropdown-open="dropdownRef?.isVisible || false" />
      <TabListDropdown ref="dropdownRef" />
    </div>
    <TabContent class="flex-110"
                @click="closeDropdown" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import TabHeader from './TabHeader.vue';
import TabListDropdown from './TabListDropdown.vue';
import TabContent from './TabContent.vue';

const dropdownRef = ref<InstanceType<typeof TabListDropdown>>();

const toggleDropdown = () => {
  dropdownRef.value?.toggle();
};

const closeDropdown = () => {
  dropdownRef.value?.close();
};

</script>

<style scoped>
.header-container {
  position: relative;
}
</style>
