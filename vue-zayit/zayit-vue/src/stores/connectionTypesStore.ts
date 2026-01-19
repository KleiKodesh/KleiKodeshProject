/**
 * Connection Types Store
 * 
 * Manages connection types and their meanings loaded from database on app startup.
 * Provides Hebrew labels and mapping for commentary filtering.
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { dbManager } from '../data/dbManager'
import type { ConnectionType } from '../types/ConnectionType'

export const useConnectionTypesStore = defineStore('connectionTypes', () => {
    // State
    const connectionTypes = ref<ConnectionType[]>([])
    const isLoaded = ref(false)
    const isLoading = ref(false)

    // Hebrew labels mapping (static since these are UI translations)
    // These map the database 'name' field to Hebrew UI labels
    const hebrewLabels: Record<string, string> = {
        'SOURCE': 'מקור',
        'OTHER': 'אחר',
        'COMMENTARY': 'מפרשים',
        'TARGUM': 'תרגומים',
        'REFERENCE': 'קשרים'
    }

    // Default connection type preference (for commentary filtering)
    const defaultConnectionType = 'COMMENTARY' // מפרשים

    // Computed getters
    const connectionTypeById = computed(() => {
        const map = new Map<number, ConnectionType>()
        connectionTypes.value.forEach(ct => map.set(ct.id, ct))
        return map
    })

    const connectionTypeByName = computed(() => {
        const map = new Map<string, ConnectionType>()
        connectionTypes.value.forEach(ct => map.set(ct.name, ct))
        return map
    })

    // Get all connection types with Hebrew labels
    const connectionTypesWithLabels = computed(() => {
        return connectionTypes.value.map(ct => ({
            ...ct,
            hebrewLabel: hebrewLabels[ct.name] || ct.name
        }))
    })

    // Actions
    const loadConnectionTypes = async () => {
        if (isLoaded.value || isLoading.value) return

        isLoading.value = true
        try {
            connectionTypes.value = await dbManager.getConnectionTypes()
            isLoaded.value = true
            console.log('✅ Connection types loaded:', connectionTypes.value.map(ct => `${ct.id}: ${ct.name} (${hebrewLabels[ct.name]})`))
        } catch (error) {
            console.error('❌ Failed to load connection types:', error)
        } finally {
            isLoading.value = false
        }
    }

    // Helper functions
    const getHebrewLabel = (connectionTypeName: string): string => {
        return hebrewLabels[connectionTypeName] || connectionTypeName
    }

    const getConnectionTypeId = (connectionTypeName: string): number | undefined => {
        return connectionTypeByName.value.get(connectionTypeName)?.id
    }

    const getConnectionTypeName = (connectionTypeId: number): string | undefined => {
        return connectionTypeById.value.get(connectionTypeId)?.name
    }

    const getDefaultConnectionTypeId = (): number | undefined => {
        return getConnectionTypeId(defaultConnectionType)
    }

    return {
        // State
        connectionTypes,
        isLoaded,
        isLoading,
        
        // Computed
        connectionTypeById,
        connectionTypeByName,
        connectionTypesWithLabels,
        
        // Actions
        loadConnectionTypes,
        
        // Helpers
        getHebrewLabel,
        getConnectionTypeId,
        getConnectionTypeName,
        getDefaultConnectionTypeId,
        
        // Constants
        defaultConnectionType
    }
})