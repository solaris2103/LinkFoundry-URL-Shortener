import { User, UserManager, WebStorageStateStore } from 'oidc-client-ts'

export const authRequired = import.meta.env.PROD || import.meta.env.VITE_AUTH_REQUIRED === 'true'

const authority = import.meta.env.VITE_OIDC_AUTHORITY
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID
const scope = import.meta.env.VITE_OIDC_SCOPE

export const authConfigured = Boolean(authority && clientId && scope)

export const userManager = authConfigured
  ? new UserManager({
    authority,
    client_id: clientId,
    redirect_uri: `${window.location.origin}/auth/callback`,
    post_logout_redirect_uri: window.location.origin,
    response_type: 'code',
    scope,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  })
  : null

export async function getAccessToken(): Promise<string | null> {
  if (!authRequired) return null
  const user = await userManager?.getUser()
  if (!user || user.expired) return null
  return user.access_token
}

export async function completeSignIn(): Promise<User | null> {
  if (!userManager) return null
  if (window.location.pathname === '/auth/callback') {
    const user = await userManager.signinRedirectCallback()
    window.history.replaceState({}, document.title, '/')
    return user
  }
  return userManager.getUser()
}