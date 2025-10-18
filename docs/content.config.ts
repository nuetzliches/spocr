import { defineCollection, defineContentConfig, z } from '@nuxt/content'

export default defineContentConfig({
  collections: {
    landing: defineCollection({
      type: 'page',
      source: 'index.md'
    }),
    docs: defineCollection({
      type: 'page',
      source: {
        include: '**',
        exclude: ['index.md']
      },
      schema: z.object({
        links: z.array(z.object({
          label: z.string(),
          icon: z.string(),
          to: z.string(),
          target: z.string().optional()
        })).optional(),
        version: z.string().optional().describe('Docs version identifier (e.g. 4.5 or 5.0)')
      })
    }),
    meta: defineCollection({
      type: 'data',
      source: 'meta',
      schema: z.object({
        versions: z.array(z.string()).optional(),
        currentVersion: z.string().optional()
      })
    })
  }
})
