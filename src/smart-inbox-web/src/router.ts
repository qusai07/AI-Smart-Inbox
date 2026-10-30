import { createRouter, createWebHistory } from 'vue-router'
import { useAuth } from './stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without signing in. */
    public?: boolean
    /** Correspondence desk only (Admin, Clerk). */
    desk?: boolean
    admin?: boolean
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('./views/LoginView.vue'), meta: { public: true } },
    { path: '/pending', name: 'pending', component: () => import('./views/PendingView.vue') },
    { path: '/', name: 'inbox', component: () => import('./views/InboxView.vue'), meta: { desk: true } },
    { path: '/documents/:id', name: 'document', component: () => import('./views/DocumentView.vue'), props: true },
    { path: '/dashboard', name: 'dashboard', component: () => import('./views/DashboardView.vue'), meta: { desk: true } },
    {
      path: '/departments/:departmentId',
      name: 'department',
      component: () => import('./views/DepartmentView.vue'),
      props: (route) => ({ departmentId: Number(route.params.departmentId) }),
    },
    { path: '/register', name: 'register', component: () => import('./views/RegisterView.vue') },
    { path: '/admin', name: 'admin', component: () => import('./views/AdminView.vue'), meta: { admin: true } },
    { path: '/integrations', name: 'integrations', component: () => import('./views/IntegrationsView.vue'), meta: { admin: true } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

// UX only: the API enforces the same rules on every request.
router.beforeEach(async (to) => {
  const auth = useAuth()
  if (!auth.checked) await auth.load()

  if (to.meta.public) return auth.me && to.name === 'login' ? auth.home : true
  if (!auth.me) return { name: 'login', query: to.fullPath === '/' ? {} : { returnUrl: to.fullPath } }
  if (auth.isPending) return to.name === 'pending' ? true : { name: 'pending' }
  if (to.name === 'pending') return auth.home

  if (to.meta.admin && !auth.isAdmin) return auth.home
  if (to.meta.desk && !auth.isDesk) return auth.home
  if (to.name === 'department' && auth.isDepartment && Number(to.params.departmentId) !== auth.me.departmentId) return auth.home
  return true
})

window.addEventListener('auth:expired', () => {
  const auth = useAuth()
  if (!auth.me) return
  auth.expired()
  router.push({ name: 'login', query: { returnUrl: router.currentRoute.value.fullPath } })
})
