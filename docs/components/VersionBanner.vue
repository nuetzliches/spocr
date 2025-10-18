<template>
    <div v-if="show"
         class="version-banner"
         :class="variantClass">
        <strong v-if="isBridge">Bridge Phase (v4.5)</strong>
        <strong v-else>Upcoming v5</strong>
        <span class="message">
            {{ message }}
        </span>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
// Content module runtime injection: global $content can be accessed if needed.
// We fallback to reading window.__NUXT__ payload for version frontmatter.

interface NuxtPageData {
    _path?: string
    version?: string
}
interface NuxtPayload {
    data?: NuxtPageData[]
}
// Access window.__NUXT__ via local safe cast

const route = useRoute()

const version = computed<string>(() => {
    const raw = (window as unknown as { __NUXT__?: { payload?: NuxtPayload } }).__NUXT__
    const nuxt: { payload?: NuxtPayload } | undefined = raw
    const pageData = nuxt?.payload?.data?.find((d: NuxtPageData) => d?._path === route.path)
    return pageData?.version || 'unknown'
})
const isBridge = computed(() => version.value === '4.5')
const isUpcoming = computed(() => version.value === '5.0')

const show = computed(() => isBridge.value || isUpcoming.value)

const message = computed(() => {
    if (isBridge.value) {
        return 'You are reading the transitional v4.5 documentation. Features and APIs may change before the v5 cutover. Planned: Legacy DataContext removal, strict determinism gate, coverage escalation.'
    }
    if (isUpcoming.value) {
        return 'This page previews v5 changes. Content may be incomplete and is subject to refinement.'
    }
    return ''
})

const variantClass = computed(() => (isBridge.value ? 'bridge' : (isUpcoming.value ? 'upcoming' : '')))
</script>

<style scoped>
.version-banner {
    padding: 0.75rem 1rem;
    border-radius: 6px;
    font-size: 0.875rem;
    line-height: 1.25rem;
    display: flex;
    gap: 0.5rem;
    align-items: center;
    margin-bottom: 1.25rem;
    border: 1px solid var(--c-border);
}

.version-banner.bridge {
    background: #fff8e1;
    border-color: #ffe082;
}

.version-banner.upcoming {
    background: #e3f2fd;
    border-color: #90caf9;
}

.version-banner strong {
    font-weight: 600;
}

.version-banner .message {
    flex: 1;
}
</style>
