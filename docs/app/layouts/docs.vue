<script setup lang="ts">
import type { ContentNavigationItem } from '@nuxt/content'
import VersionBanner from '~/components/VersionBanner.vue'

const navigation = inject<Ref<ContentNavigationItem[]>>('navigation')
</script>

<template>
  <UContainer>
    <UPage>
      <template #left>
        <UPageAside>
          <UContentNavigation highlight
                              :navigation="navigation" />
        </UPageAside>
      </template>
      <VersionBanner />
      <slot />
    </UPage>
  </UContainer>
</template>
