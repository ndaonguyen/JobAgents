// Client-side mirror of SourceHostUtil: friendly host + listing-page heuristic for posting links.

export function sourceHost(url?: string | null): string {
  if (!url) return 'other';
  try {
    const host = new URL(url).host;
    return host.startsWith('www.') ? host.slice(4) : host;
  } catch {
    return 'other';
  }
}

export function isListing(url?: string | null): boolean {
  if (!url) return false;
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  const path = parsed.pathname.toLowerCase();
  const query = parsed.search.toLowerCase();
  if (path.includes('tim-viec-lam') || path.includes('/tim-kiem') || path.includes('/search') || path.includes('/it-jobs'))
    return true;
  if (/-kl\d+\/?$/.test(path)) return true;
  return query.includes('q=') || query.includes('keyword') || query.includes('search') || query.includes('page=');
}
