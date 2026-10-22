// Args: pattern (regex for a saved conversation's link).
// Returns the site's recent conversations from its sidebar: [{title, url}], newest first.
const re = new RegExp(pattern), out = [], seen = new Set();
for (const a of document.querySelectorAll('a[href]')) {
  const h = a.getAttribute('href');
  if (!re.test(h)) continue;
  const url = new URL(h, location.href).href;
  const title = (a.innerText || a.textContent || '').trim().split('\n')[0].trim();
  if (!title || seen.has(url)) continue;
  seen.add(url);
  out.push({title, url});
}
return out.slice(0, 40);
