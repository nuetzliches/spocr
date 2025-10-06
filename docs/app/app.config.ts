export default defineAppConfig({
  ui: {
    colors: {
      primary: 'blue',
      neutral: 'slate'
    },
    footer: {
      slots: {
        root: 'border-t border-default',
        left: 'text-sm text-muted'
      }
    }
  },
  seo: {
    siteName: 'SpocR Documentation'
  },
  header: {
    title: 'SpocR',
    to: '/',
    logo: {
      alt: 'SpocR',
      light: '',
      dark: ''
    },
    search: true,
    colorMode: true,
    links: [{
      'icon': 'i-simple-icons-github',
      'to': 'https://github.com/nuetzliches/spocr',
      'target': '_blank',
      'aria-label': 'GitHub'
    }]
  },
  footer: {
    credits: `Built with Nuxt UI • © ${new Date().getFullYear()} SpocR`,
    colorMode: false,
    links: [{
      'icon': 'i-simple-icons-github',
      'to': 'https://github.com/nuetzliches/spocr',
      'target': '_blank',
      'aria-label': 'SpocR on GitHub'
    }]
  },
  toc: {
    title: 'Table of Contents',
    bottom: {
      title: 'Community',
      edit: 'https://github.com/nuetzliches/spocr/edit/main/docs/content',
      links: [{
        icon: 'i-lucide-star',
        label: 'Star on GitHub',
        to: 'https://github.com/nuetzliches/spocr',
        target: '_blank'
      }, {
        icon: 'i-lucide-package',
        label: 'NuGet Package',
        to: 'https://www.nuget.org/packages/SpocR',
        target: '_blank'
      }]
    }
  }
})
