// Args: composer (CSS selector). Returns true (signed in), false (sign-in page / login buttons shown), or null (still loading).
const vis = e => e && e.getClientRects().length > 0;
if (/\/(login|logout|auth|signin|sign-in)/i.test(location.pathname) || /^(accounts|auth|login)\./.test(location.hostname)) return false;
const loginButton = [...document.querySelectorAll('a, button')].some(e => vis(e) &&
  /^(log ?in|sign ?in|sign up|get started)$/i.test((e.innerText || e.getAttribute('aria-label') || '').trim()));
if (loginButton) return false;
return document.querySelector(composer) ? true : null;
