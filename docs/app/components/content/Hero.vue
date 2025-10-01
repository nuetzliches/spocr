<script setup lang="ts">
interface Props {
  title?: string
  description?: string
  headline?: string
  links?: Array<{
    label: string
    to: string
    size?: string
    color?: string
    variant?: string
    icon?: string
    target?: string
  }>
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  description: '',
  headline: '',
  links: () => []
})
</script>

<template>
  <div class="relative overflow-hidden bg-gradient-to-br from-white to-gray-50 dark:from-gray-900 dark:to-gray-800">
    <!-- Background -->
    <HeroBackground />
    
    <!-- Content -->
    <div class="relative z-10 py-24 sm:py-32">
      <div class="mx-auto max-w-7xl px-6 lg:px-8">
        <div class="mx-auto max-w-2xl text-center">
          <p v-if="props.headline" class="text-base font-semibold leading-7 text-primary">
            {{ props.headline }}
          </p>
          <h1 class="mt-2 text-4xl font-bold tracking-tight text-gray-900 dark:text-white sm:text-6xl">
            {{ props.title }}
          </h1>
          <p class="mt-6 text-lg leading-8 text-gray-600 dark:text-gray-300">
            {{ props.description }}
          </p>
          <div v-if="props.links?.length" class="mt-10 flex items-center justify-center gap-x-6">
            <UButton
              v-for="link in props.links"
              :key="link.label"
              :to="link.to"
              :size="link.size || 'lg'"
              :color="link.color || 'primary'"
              :variant="link.variant || 'solid'"
              :icon="link.icon"
              :target="link.target"
            >
              {{ link.label }}
            </UButton>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>