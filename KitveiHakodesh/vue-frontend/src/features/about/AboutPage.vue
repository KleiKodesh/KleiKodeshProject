<script setup lang="ts">
/**
 * The About page (the 'about' widget window, opened from Settings -> About): the app's name and
 * installed version, then the credits and licenses. Kitvei Hakodesh's own page; the Word add-in
 * keeps its separate About.*.html.
 */
import { onMounted } from 'vue'
import { useSelfUpdate } from '@/composables/useSelfUpdate'
import AboutPageCredits from './AboutPageCredits.vue'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

const { installedVersion, loadUpdateStatus } = useSelfUpdate()
onMounted(() => void loadUpdateStatus())

const hostWindow = useHostWindow()

/** Same contact list as the Word add-in's About page (section "Contact"). */
const CONTACT_LINKS = [
  { key: 'issues', url: 'https://github.com/KleiKodesh/KleiKodeshProject/issues/new/choose', text: 'github.com/KleiKodesh/KleiKodeshProject' },
  { key: 'website', url: 'https://kleikodesh.github.io/', text: 'kleikodesh.github.io' },
  { key: 'projects', url: 'https://github.com/KleiKodesh', text: 'github.com/KleiKodesh' },
  { key: 'email', url: 'mailto:kleikodeshproject@gmail.com', text: 'kleikodeshproject@gmail.com' },
]

function openLink(event: MouseEvent, url: string) {
  event.preventDefault()
  void openUrlInSystemBrowser(url).then((ok) => {
    if (!ok) hostWindow.open(url, '_blank', 'noopener')
  })
}
</script>

<template>
  <div class="about-page" data-testid="about-page">
    <div class="about-inner">
      <header class="about-header">
        <h1 class="about-app-name">{{ $t('settings.about.appName') }}</h1>
        <p v-if="installedVersion" class="about-version" data-testid="about-version">
          {{ $t('settings.about.version', { version: installedVersion }) }}
        </p>
        <p class="about-text">{{ $t('settings.about.what') }}</p>
        <p class="about-text">{{ $t('settings.about.volunteers') }}</p>
      </header>

      <section class="about-contact">
        <h2 class="about-h2">{{ $t('settings.about.contactTitle') }}</h2>
        <p class="about-text">{{ $t('settings.about.contactIntro') }}</p>
        <ul class="about-links">
          <li v-for="link in CONTACT_LINKS" :key="link.key">
            {{ $t(`settings.about.${link.key}`) }}:
            <a dir="ltr" :href="link.url" @click="openLink($event, link.url)">{{ link.text }}</a>
          </li>
        </ul>
      </section>

      <AboutPageCredits />
    </div>
  </div>
</template>

<style scoped>
.about-page {
  height: 100%;
  overflow-y: auto;
  background: var(--bg-primary);
  container: about-page / inline-size;
}
.about-inner {
  max-width: 900px;
  margin: 0 auto;
  padding: 16px 16px 24px;
}
.about-header {
  margin-bottom: 20px;
}
.about-app-name {
  margin: 0;
  font-size: 24px;
  font-weight: 700;
  color: var(--text-primary);
}
.about-text {
  margin: 8px 0 0;
  font-size: 13px;
  color: var(--text-primary);
}
.about-contact {
  margin-bottom: 24px;
  font-size: 13px;
  color: var(--text-primary);
}
.about-h2 {
  margin: 0;
  font-size: 17px;
  font-weight: 700;
}
.about-links {
  margin: 6px 0 0;
  padding-inline-start: 18px;
}
.about-links li {
  margin: 3px 0;
}
.about-links a {
  color: var(--accent-color);
}
.about-version {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--text-secondary);
}
</style>
