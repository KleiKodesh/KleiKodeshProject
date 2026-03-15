import { createRouter, createWebHashHistory } from 'vue-router'
import HomePage from '@/components/home/HomePage.vue'
import PdfViewPage from '@/components/pdf/PdfViewPage.vue'

export default createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', component: HomePage, meta: { title: 'בית' } },
    { path: '/pdf-view', component: PdfViewPage, meta: { title: 'צפייה בקובץ' } },
  ]
})
