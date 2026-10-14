// Args: composer, send, reply (CSS selectors), b64 (PNG or ""), text.
// Waits for the composer, clears any draft, attaches the image, types, presses send.
// Returns {before} (reply count before sending) or {error}.
const sleep = ms => new Promise(r => setTimeout(r, ms));
const $ = s => document.querySelector(s);
let ed = null;
for (let i = 0; i < 80 && !(ed = $(composer)); i++) await sleep(250);
if (!ed) return {error: 'not-signed-in'};
const before = document.querySelectorAll(reply).length;
// Clear any saved draft left in the box.
ed.focus();
{ const s0 = window.getSelection(); s0.selectAllChildren(ed); document.execCommand('delete'); }
if (b64) {
  const bin = atob(b64), u8 = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
  const file = new File([u8], 'snapshot.png', {type: 'image/png', lastModified: Date.now()});
  const mk = () => { const dt = new DataTransfer(); dt.items.add(file); return dt; };
  const previews = () => document.querySelectorAll('img[src^="blob:"], img[src^="data:image"], form img').length;
  const base = previews();
  const appeared = async () => { for (let i = 0; i < 12; i++) { if (previews() > base) return true; await sleep(250); } return false; };
  ed.focus();
  let ok = false;
  try { ed.dispatchEvent(new ClipboardEvent('paste', {clipboardData: mk(), bubbles: true, cancelable: true})); } catch (e) {}
  ok = await appeared();
  if (!ok) for (const inp of document.querySelectorAll('input[type=file]')) {
    if (inp.accept && !(inp.accept.includes('image') || inp.accept.includes('*'))) continue;
    try { inp.files = mk().files; inp.dispatchEvent(new Event('change', {bubbles: true})); } catch (e) {}
    if (ok = await appeared()) break;
  }
  if (!ok) {
    try { const dt = mk(); for (const t of ['dragenter', 'dragover', 'drop'])
      ed.dispatchEvent(new DragEvent(t, {dataTransfer: dt, bubbles: true, cancelable: true})); } catch (e) {}
    ok = await appeared();
  }
  if (!ok) return {error: 'attach-failed'};
}
ed.focus();
const s = window.getSelection(); s.selectAllChildren(ed); s.collapseToEnd();
document.execCommand('insertText', false, text);
await sleep(150);
let tried = false;
for (let i = 0; i < 240; i++) {      // waits for the image upload to finish
  if (tried && (!ed.isConnected || !ed.innerText.trim())) return {before};   // sent (by click or Enter)
  const b = [...document.querySelectorAll(send)].find(b =>
    !b.disabled && b.getAttribute('aria-disabled') !== 'true' && b.getClientRects().length > 0);
  if (b) {
    tried = true;
    b.click();
    // Confirm it actually went: the box empties once the message is sent. Otherwise retry (or press Enter).
    for (let k = 0; k < 8; k++) {
      await sleep(150);
      if (!ed.isConnected || !ed.innerText.trim()) return {before};
    }
    if (i % 4 === 3) ed.dispatchEvent(new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true}));
    continue;
  }
  await sleep(250);
}
return {error: 'send-timeout'};
