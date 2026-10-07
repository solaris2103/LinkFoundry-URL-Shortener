import { FormEvent, useEffect, useState } from 'react'
import { ArrowDownRight, ArrowUpRight, BarChart3, Check, Clock3, Copy, ExternalLink, Link2, LoaderCircle, LogIn, LogOut, Plus, Radio, ShieldCheck, Trash2 } from 'lucide-react'
import { authConfigured, authRequired, completeSignIn, getAccessToken, userManager } from './auth'
import './App.css'

type LinkSummary = {
  code: string
  shortUrl: string
  destinationUrl: string
  createdAt: string
  expiresAt: string | null
  clickCount: number
  isActive: boolean
}

type LinkAnalytics = {
  link: LinkSummary
  dailyClicks: { date: string; clicks: number }[]
  topReferrers: { host: string; clicks: number }[]
}

const apiBase = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const accessToken = await getAccessToken()
  if (authRequired && !accessToken) throw new Error('Sign in to continue.')
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...init?.headers,
    },
  })
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string; errors?: Record<string, string[]> } | null
    const message = body?.error ?? Object.values(body?.errors ?? {}).flat()[0] ?? `Request failed (${response.status})`
    throw new Error(message)
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(value))
}

function isLinkExpired(link: LinkSummary, now: number) {
  return link.expiresAt !== null && new Date(link.expiresAt).getTime() <= now
}

function App() {
  const [links, setLinks] = useState<LinkSummary[]>([])
  const [selectedCode, setSelectedCode] = useState<string | null>(null)
  const [analytics, setAnalytics] = useState<LinkAnalytics | null>(null)
  const [analyticsCode, setAnalyticsCode] = useState<string | null>(null)
  const [destination, setDestination] = useState('')
  const [customCode, setCustomCode] = useState('')
  const [expiry, setExpiry] = useState('')
  const [isCreating, setIsCreating] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  const [apiOnline, setApiOnline] = useState<boolean | null>(null)
  const [currentTime, setCurrentTime] = useState(() => Date.now())
  const [authReady, setAuthReady] = useState(!authRequired)
  const [signedIn, setSignedIn] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [copiedCode, setCopiedCode] = useState<string | null>(null)

  async function refreshLinks() {
    const result = await request<LinkSummary[]>('/api/links')
    setLinks(result)
    setApiOnline(true)
    setSelectedCode(current => current && result.some(link => link.code === current) ? current : result[0]?.code ?? null)
  }

  useEffect(() => {
    if (!authRequired) return
    completeSignIn()
      .then(user => setSignedIn(Boolean(user && !user.expired)))
      .catch(reason => setError(reason instanceof Error ? reason.message : 'Sign-in could not be completed.'))
      .finally(() => setAuthReady(true))
  }, [])

  useEffect(() => {
    const intervalId = window.setInterval(() => setCurrentTime(Date.now()), 30_000)
    return () => window.clearInterval(intervalId)
  }, [])

  useEffect(() => {
    if (authRequired && (!authReady || !signedIn)) return
    let cancelled = false
    request<LinkSummary[]>('/api/links')
      .then(result => {
        if (cancelled) return
        setLinks(result)
        setApiOnline(true)
        setSelectedCode(current => current && result.some(link => link.code === current) ? current : result[0]?.code ?? null)
      })
      .catch(reason => {
        if (cancelled) return
        setApiOnline(false)
        setError(reason instanceof Error ? reason.message : 'The API could not be reached.')
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })
    return () => { cancelled = true }
  }, [authReady, signedIn])

  useEffect(() => {
    if (!selectedCode || (authRequired && (!authReady || !signedIn))) return
    let cancelled = false
    request<LinkAnalytics>(`/api/links/${encodeURIComponent(selectedCode)}`)
      .then(result => {
        if (cancelled) return
        setAnalytics(result)
        setAnalyticsCode(selectedCode)
      })
      .catch(reason => {
        if (!cancelled) setError(reason instanceof Error ? reason.message : 'Analytics could not be loaded.')
      })
    return () => { cancelled = true }
  }, [selectedCode, links, authReady, signedIn])

  async function apiSignIn() {
    await userManager?.signinRedirect()
  }

  async function apiSignOut() {
    await userManager?.signoutRedirect()
  }

  async function createLink(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setIsCreating(true)
    setError('')
    setNotice('')
    try {
      const created = await request<LinkSummary>('/api/links', {
        method: 'POST',
        body: JSON.stringify({
          destinationUrl: destination,
          customCode: customCode.trim() || null,
          expiresAt: expiry ? new Date(expiry).toISOString() : null,
        }),
      })
      setDestination('')
      setCustomCode('')
      setExpiry('')
      await refreshLinks()
      setSelectedCode(created.code)
      setNotice(`/${created.code} is ready to share.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The link could not be created.')
    } finally {
      setIsCreating(false)
    }
  }

  async function copyLink(link: LinkSummary) {
    try {
      await navigator.clipboard.writeText(link.shortUrl)
      setCopiedCode(link.code)
      window.setTimeout(() => setCopiedCode(current => current === link.code ? null : current), 1800)
    } catch {
      setError('Clipboard access was blocked by the browser.')
    }
  }

  async function deactivateLink(code: string) {
    if (!window.confirm(`Deactivate /${code}? Existing links will stop redirecting.`)) return
    try {
      await request<void>(`/api/links/${encodeURIComponent(code)}`, { method: 'DELETE' })
      await refreshLinks()
      setNotice(`/${code} has been deactivated.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The link could not be deactivated.')
    }
  }

  const totalClicks = links.reduce((sum, link) => sum + link.clickCount, 0)
  const activeLinks = links.filter(link => link.isActive && !isLinkExpired(link, currentTime)).length
  const daily = analytics?.dailyClicks ?? []
  const maxDaily = Math.max(1, ...daily.map(item => item.clicks))
  const visibleAnalytics = analyticsCode === selectedCode ? analytics : null

  return (
    <div className="app-shell">
      <aside className="rail">
        <a className="brand-mark" href="#top" aria-label="LinkFoundry home"><Link2 size={21} strokeWidth={2.4} /></a>
        <div className="rail-rule" />
        <button className="rail-action active" title="Links" aria-label="Links"><Link2 size={19} /></button>
        <button className="rail-action" title="Analytics" aria-label="Analytics" onClick={() => document.querySelector('#analytics')?.scrollIntoView({ behavior: 'smooth' })}><BarChart3 size={19} /></button>
        <div className="rail-bottom"><span className="connection-dot" title="API connected when data loads" /></div>
      </aside>

      <main id="top" className="main-area">
        <header className="topbar">
          <div className="wordmark"><span className="wordmark-icon"><Link2 size={15} /></span> LinkFoundry <span className="wordmark-divider">/</span> workspace</div>
          <div className="topbar-right">{authRequired && signedIn && <button className="signout-button" onClick={() => void apiSignOut()} title="Sign out"><LogOut size={14} /><span>Sign out</span></button>}<span className="avatar">E</span></div>
        </header>

        <div className="page-content">
          {authRequired && (!authReady || !signedIn) && <section className="auth-gate" aria-live="polite">
            <div className="auth-emblem"><ShieldCheck size={25} /></div>
            <h1>{!authConfigured ? 'Identity provider required' : authReady ? 'Sign in to LinkFoundry' : 'Checking your session'}</h1>
            <p>{!authConfigured ? 'Set the OIDC authority, client ID, and API scope before enabling production access.' : 'Link management and analytics are available to authenticated workspace members.'}</p>
            {authConfigured && authReady && <button className="create-button" onClick={() => void apiSignIn()}><LogIn size={16} /> Sign in</button>}
          </section>}
          {(!authRequired || (authReady && signedIn)) && <>
          <section className="page-heading">
            <div>
              <div className="eyebrow"><Radio size={13} /> LINK OPERATIONS</div>
              <h1>Make every link <span>count.</span></h1>
              <p className="subtitle">Create, monitor, and manage your short links.</p>
            </div>
            <div className="status-stamp"><ShieldCheck size={15} /> ENGINEER CONTROLLED</div>
          </section>

          {(error || notice) && <div className={`message ${error ? 'message-error' : 'message-success'}`} role="status">{error || notice}<button onClick={() => { setError(''); setNotice('') }} aria-label="Dismiss message">×</button></div>}

          <section className="metric-strip" aria-label="Workspace metrics">
            <div className="metric"><span className="metric-label">LINKS CREATED</span><strong>{links.length.toString().padStart(2, '0')}</strong><span className="metric-foot">up to 100 most recent</span></div>
            <div className="metric"><span className="metric-label">TOTAL REDIRECTS</span><strong>{totalClicks.toLocaleString()}</strong><span className="metric-foot"><ArrowUpRight size={13} /> all-time recorded clicks</span></div>
            <div className="metric"><span className="metric-label">ACTIVE LINKS</span><strong>{activeLinks.toString().padStart(2, '0')}</strong><span className="metric-foot"><span className={`live-dot ${apiOnline === false ? 'offline' : ''}`} /> {apiOnline === null ? 'checking API' : apiOnline ? 'API responding' : 'API unavailable'}</span></div>
            <div className="metric metric-note"><span className="metric-label">SYSTEM STATUS</span><strong className={apiOnline === false ? 'status-down' : 'status-ok'}>{apiOnline === null ? 'Checking' : apiOnline ? 'Operational' : 'Unavailable'}</strong><span className="metric-foot">SQLite · 30-day analytics window</span></div>
          </section>

          <section className="create-section" aria-labelledby="create-heading">
            <div className="section-kicker"><span>01</span><span className="kicker-line" /> NEW SHORT LINK</div>
            <div className="create-layout">
              <div className="create-copy"><h2 id="create-heading">Give a long URL<br />a shorter life.</h2><p>Destinations must use HTTP or HTTPS. Custom codes are public and case-sensitive.</p></div>
              <form className="create-form" onSubmit={createLink}>
                <label className="field-label" htmlFor="destination">DESTINATION URL <span>required</span></label>
                <div className="url-field"><Link2 size={17} /><input id="destination" type="url" placeholder="https://example.com/your/very/long/link" value={destination} onChange={event => setDestination(event.target.value)} maxLength={2048} required /></div>
                <div className="form-row">
                  <div className="form-field"><label className="field-label" htmlFor="custom-code">CUSTOM CODE <span>optional</span></label><div className="code-field"><span>go/</span><input id="custom-code" placeholder="spring-launch" value={customCode} onChange={event => setCustomCode(event.target.value)} minLength={3} maxLength={32} pattern={'[A-Za-z0-9_\\-]{3,32}'} title="Use 3-32 letters, numbers, hyphens, or underscores" /></div></div>
                  <div className="form-field"><label className="field-label" htmlFor="expiry">EXPIRES <span>optional</span></label><div className="expiry-field"><Clock3 size={15} /><input id="expiry" type="datetime-local" value={expiry} onChange={event => setExpiry(event.target.value)} min={new Date().toISOString().slice(0, 16)} /></div></div>
                  <button className="create-button" type="submit" disabled={isCreating}>{isCreating ? <LoaderCircle className="spin" size={17} /> : <Plus size={17} />}<span>{isCreating ? 'Creating' : 'Create link'}</span></button>
                </div>
              </form>
            </div>
          </section>

          <section className="workspace-grid">
            <div className="links-section">
              <div className="section-header"><div><div className="section-kicker"><span>02</span><span className="kicker-line" /> YOUR LINKS</div><h2>Recent links <span className="count-chip">{links.length}</span></h2></div><span className="list-caption">LATEST FIRST · MAX 100</span></div>
              <div className="link-table" role="table" aria-label="Recent short links">
                <div className="table-head" role="row"><span>SHORT LINK</span><span>DESTINATION</span><span>CLICKS</span><span>STATUS</span><span aria-label="Actions" /></div>
                {isLoading ? <div className="empty-state"><LoaderCircle className="spin" size={20} /> Loading links…</div> : links.length === 0 ? <div className="empty-state"><Link2 size={21} /><strong>No links yet</strong><span>Create your first short link above.</span></div> : links.map(link => <div className={`link-row ${selectedCode === link.code ? 'selected' : ''}`} key={link.code} role="row" onClick={() => setSelectedCode(link.code)}>
                  <div className="short-cell"><button className="short-link" onClick={event => { event.stopPropagation(); setSelectedCode(link.code) }}>{link.shortUrl.replace(/^https?:\/\//, '')}</button><span className="created-date">{formatDate(link.createdAt)}</span></div>
                  <a className="destination" href={link.destinationUrl} target="_blank" rel="noreferrer" title={link.destinationUrl} onClick={event => event.stopPropagation()}>{link.destinationUrl.replace(/^https?:\/\//, '').replace(/\/$/, '')}<ExternalLink size={12} /></a>
                  <span className="click-count">{link.clickCount.toLocaleString()}</span>
                  <span className={`state-pill ${link.isActive && !isLinkExpired(link, currentTime) ? 'state-active' : isLinkExpired(link, currentTime) ? 'state-expired' : 'state-disabled'}`}><span />{isLinkExpired(link, currentTime) ? 'Expired' : link.isActive ? 'Active' : 'Inactive'}</span>
                  <div className="row-actions"><button className="icon-button" title="Copy short link" aria-label={`Copy ${link.code}`} onClick={event => { event.stopPropagation(); void copyLink(link) }}>{copiedCode === link.code ? <Check size={15} /> : <Copy size={15} />}</button><button className="icon-button" title={isLinkExpired(link, currentTime) ? 'Link has expired' : 'Open short link'} aria-label={`Open ${link.code}`} disabled={isLinkExpired(link, currentTime)} onClick={event => { event.stopPropagation(); window.open(link.shortUrl, '_blank', 'noopener,noreferrer') }}><ArrowUpRight size={15} /></button>{link.isActive && !isLinkExpired(link, currentTime) && <button className="icon-button danger-action" title="Deactivate link" aria-label={`Deactivate ${link.code}`} onClick={event => { event.stopPropagation(); void deactivateLink(link.code) }}><Trash2 size={14} /></button>}</div>
                </div>)}
              </div>
            </div>

            <aside id="analytics" className="analytics-panel">
              <div className="section-kicker"><span>03</span><span className="kicker-line" /> LINK INTELLIGENCE</div>
              {visibleAnalytics ? <>
                <div className="analytics-title"><div><h2>Performance</h2><span className="analytics-code">/{visibleAnalytics.link.code}</span></div><span className="analytics-period">30 DAYS</span></div>
                <div className="analytics-total"><strong>{visibleAnalytics.link.clickCount.toLocaleString()}</strong><span>total redirects</span></div>
                <div className="chart-wrap" aria-label="Daily redirects in the last 30 days">
                  {daily.length ? <div className="bar-chart">{daily.slice(-14).map(item => <div className="bar-column" key={item.date} title={`${item.date}: ${item.clicks} clicks`}><span style={{ height: `${Math.max(5, item.clicks / maxDaily * 100)}%` }} /><small>{new Date(`${item.date}T00:00:00`).toLocaleDateString('en', { day: 'numeric' })}</small></div>)}</div> : <div className="chart-empty"><ArrowDownRight size={16} /> Clicks will appear here</div>}
                </div>
                <div className="analytics-divider" />
                <div className="referrer-heading"><span>TOP REFERRERS</span><span>CLICKS</span></div>
                {visibleAnalytics.topReferrers.length ? visibleAnalytics.topReferrers.map(referrer => <div className="referrer-row" key={referrer.host}><span><i />{referrer.host}</span><strong>{referrer.clicks}</strong></div>) : <p className="no-referrers">Referrer data appears after a visit includes a referrer.</p>}
                <div className="analytics-footer"><Clock3 size={13} /> Created {formatDate(visibleAnalytics.link.createdAt)}{visibleAnalytics.link.expiresAt && <span> · Expires {formatDate(visibleAnalytics.link.expiresAt)}</span>}</div>
              </> : <div className="analytics-placeholder"><BarChart3 size={25} /><strong>Select a link</strong><span>Choose a link to inspect its redirect activity and referrers.</span></div>}
            </aside>
          </section>

          <footer className="page-footer"><span>LINKFOUNDRY <span className="footer-dot">·</span> ENGINEERING ASSESSMENT</span><span>Engineer-owned execution <span className="footer-dot">·</span> Every change reviewable</span></footer>
          </>}
        </div>
      </main>
    </div>
  )
}

export default App
