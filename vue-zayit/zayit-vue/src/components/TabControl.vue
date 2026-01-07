<template>
  <div class="flex-column height-fill">
    <div class="header-container">
      <TabHeader @click="toggleDropdown"
                 @close-tab-dropdown="closeDropdown"
                 :is-dropdown-open="dropdownRef?.isVisible || false" />
      <TabDropdown ref="dropdownRef" />
    </div>
    <TabContent class="flex-110"
                @click="closeDropdown" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import TabHeader from './TabHeader.vue';
import TabDropdown from './TabDropdown.vue';
import TabContent from './TabContent.vue';

const dropdownRef = ref<InstanceType<typeof TabDropdown>>();

const toggleDropdown = () => {
  dropdownRef.value?.toggle();
};

const closeDropdown = () => {
  dropdownRef.value?.close();
};

</script>
