// Same-origin cookies stay HttpOnly. Only the antiforgery request token is visible to JS.
window.community = (() => {
  let csrf = '', root = null, routeListener = null, nativeSearch = false, signedIn = false;
  const streams = new Map();
  async function request(path, method = 'GET', body = null) {
    const headers = { Accept: 'application/json' };
    if (body !== null) headers['Content-Type'] = 'application/json';
    if (!['GET', 'HEAD'].includes(method)) headers['X-CSRF'] = csrf;
    const response = await fetch(path, { method, credentials: 'same-origin', headers,
      body: body === null ? undefined : body, cache: 'no-store' });
    const raw = await response.text();
    if (!response.ok) {
      let message = response.status === 429 ? 'Too many requests. Please wait a moment.' : `Request failed (${response.status}).`;
      try { const data = JSON.parse(raw); message = data.error || data.detail || message; } catch {}
      throw new Error(message);
    }
    if (!raw) return '{}';
    const value = JSON.parse(raw);
    if (value.csrfToken) csrf = value.csrfToken;
    if (path === 'api/site') nativeSearch = value.nativeSearch === true;
    if (path === 'api/auth/session') signedIn = !!value.user;
    if (path === 'api/auth/logout') signedIn = false;
    return JSON.stringify(value);
  }
  async function search(body, signal) {
    if (!nativeSearch || !signedIn) return null;
    const response = await fetch('api/engine/search', { method: 'POST', credentials: 'same-origin',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF': csrf },
      body, signal, cache: 'no-store' });
    // Guests, static hosting, disabled engines and a full worker pool use Wasm.
    if ([401, 403, 404, 429, 503].includes(response.status)) return null;
    const raw = await response.text();
    if (!response.ok) {
      let message = 'Server search failed.';
      try { message = JSON.parse(raw).error || message; } catch {}
      throw new Error(message);
    }
    return raw;
  }
  function watch(slot, game, reference) {
    stop(slot);
    const stream = new EventSource('api/events' + (game ? '?game=' + encodeURIComponent(game) : ''), { withCredentials: true });
    stream.onmessage = event => { if (event.data !== 'heartbeat') reference.invokeMethodAsync('CommunityChanged').catch(() => stop(slot)); };
    streams.set(slot, stream);
  }
  function stop(slot) { const stream = streams.get(slot); if (stream) stream.close(); streams.delete(slot); }
  function init(reference) {
    root = reference;
    if (routeListener) window.removeEventListener('hashchange', routeListener);
    routeListener = () => root.invokeMethodAsync('CommunityNavigate', location.hash.slice(1) || 'play');
    window.addEventListener('hashchange', routeListener);
    return location.hash.slice(1) || 'play';
  }
  function dispose() { for (const key of streams.keys()) stop(key); if (routeListener) window.removeEventListener('hashchange', routeListener); root = null; }
  return { request, search, watch, stop, init, dispose, go: route => { location.hash = route; },
    copy: value => navigator.clipboard.writeText(value),
    share: route => new URL('#' + route, document.baseURI).href,
    now: () => performance.now() };
})();
