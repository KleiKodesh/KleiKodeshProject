<script setup lang="ts">
/**
 * About page -> credits and licenses: the components and open data the app uses (credits.ts),
 * the attribution line the Zayit license requires (shown first, never hidden), and a button
 * that opens the full THIRD-PARTY-NOTICES.txt shipped in the app folder (found by the service).
 */
import { ref } from 'vue'
import { CREDIT_GROUPS, ZAYIT_ATTRIBUTION } from './credits'
import { getThirdPartyNoticesPath, openFileInDefaultApp, openUrlInSystemBrowser } from '@/webview-host/bridge'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

const hostWindow = useHostWindow()
const noticesMissing = ref(false)

async function openNotices() {
  const path = await getThirdPartyNoticesPath()
  noticesMissing.value = !path || !(await openFileInDefaultApp(path))
}

function openLink(event: MouseEvent, url: string) {
  event.preventDefault()
  void openUrlInSystemBrowser(url).then((ok) => {
    if (!ok) hostWindow.open(url, '_blank', 'noopener')
  })
}
</script>

<template>
  <section class="credits" data-testid="credits">
    <h2 class="credits-title">{{ $t('settings.credits.section') }}</h2>
    <p class="credits-zayit" dir="ltr" data-testid="credits-zayit">{{ ZAYIT_ATTRIBUTION }}</p>
    <p class="credits-intro">{{ $t('settings.about.otzariaPermission') }}</p>
    <p class="credits-intro">{{ $t('settings.about.booksOwnLicense') }}</p>
    <p class="credits-intro">{{ $t('settings.credits.intro') }}</p>

    <div v-for="group in CREDIT_GROUPS" :key="group.id" class="credits-group">
      <h3 class="credits-group-title">{{ $t(`settings.credits.groups.${group.id}`) }}</h3>
      <ul class="credits-list">
        <li v-for="item in group.items" :key="item.id" class="credits-row" :data-credit="item.id">
          <span class="credits-name" dir="ltr">{{ item.name }}</span>
          <span class="credits-use">{{ $t(`settings.credits.use.${item.id}`) }}</span>
          <span class="credits-license" dir="ltr">{{ item.license }}</span>
          <a class="credits-link" dir="ltr" :href="item.url" @click="openLink($event, item.url)">{{ item.url.replace(/^https?:\/\//, '').replace(/\/$/, '') }}</a>
        </li>
      </ul>
    </div>

    <p class="credits-more">{{ $t('settings.credits.packagesNote') }}</p>
    <button class="about-btn" data-testid="open-notices" @click="openNotices">{{ $t('settings.credits.openNotices') }}</button>
    <p v-if="noticesMissing" class="credits-missing">{{ $t('settings.credits.noticesMissing') }}</p>
  </section>
</template>

<style scoped>
.credits {
  font-size: 13px;
  color: var(--text-primary);
}
.credits-title {
  margin: 0 0 10px;
  font-size: 17px;
  font-weight: 700;
}
.credits-intro,
.credits-more,
.credits-missing {
  margin: 0 0 8px;
  color: var(--text-secondary);
}
.credits-zayit {
  margin: 0 0 12px;
  font-weight: 600;
  text-align: start;
}
.credits-group-title {
  margin: 14px 0 6px;
  font-size: 13.5px;
  font-weight: 700;
}
.credits-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.credits-row {
  display: grid;
  grid-template-columns: minmax(140px, 1.2fr) 2fr minmax(110px, 1fr) minmax(120px, 1fr);
  gap: 4px 12px;
  padding: 5px 0;
  border-bottom: 1px solid var(--border-color);
  align-items: baseline;
}
.credits-name {
  font-weight: 600;
  text-align: start;
}
.credits-use,
.credits-license {
  color: var(--text-secondary);
}
.credits-link {
  color: var(--accent-color);
  overflow-wrap: anywhere;
  text-align: start;
}
.about-btn {
  margin-top: 4px;
  height: 28px;
  padding: 0 12px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  cursor: pointer;
}
.about-btn:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
@container about-page (max-width: 620px) {
  .credits-row {
    grid-template-columns: 1fr;
  }
}
</style>
