// Args: reply, stop (CSS selectors), before (reply count before sending).
// Returns the newest reply as lightweight markdown + whether it is still streaming.
const msgs = document.querySelectorAll(reply);
if (msgs.length <= before) return {text: '', streaming: true};
const root = msgs[msgs.length - 1];
const out = [];
const block = new Set(['P','DIV','UL','OL','TABLE','TR','BLOCKQUOTE','H1','H2','H3','H4','H5','H6','SECTION']);
const depth = n => { let d = -1; for (let p = n; p && p !== root; p = p.parentElement) if (p.tagName === 'UL' || p.tagName === 'OL') d++; return Math.max(d, 0); };
const walk = n => {
  if (n.nodeType === 3) { out.push(n.nodeValue); return; }
  if (n.nodeType !== 1) return;
  const t = n.tagName;
  if (['BUTTON', 'svg', 'STYLE', 'SCRIPT', 'NOSCRIPT'].includes(t)) return;
  if (n.classList.contains('katex')) {
    const a = n.querySelector('annotation');
    out.push(a ? (n.closest('.katex-display') ? '\n$$' + a.textContent + '$$\n' : '$' + a.textContent + '$') : n.innerText);
    return;
  }
  if (t === 'PRE') { out.push('\n```\n' + (n.querySelector('code') || n).innerText.trim() + '\n```\n'); return; }
  if (t === 'CODE') { out.push('`' + n.innerText + '`'); return; }
  if (t === 'BR') { out.push('\n'); return; }
  if (/^H[1-6]$/.test(t)) out.push('\n## ');
  if (t === 'LI') {
    const p = n.parentElement;
    out.push('\n' + '  '.repeat(depth(n)) + (p && p.tagName === 'OL' ? ([...p.children].indexOf(n) + 1) + '. ' : '• '));
  }
  const wrap = {STRONG: '**', B: '**', EM: '*'}[t];
  if (wrap) out.push(wrap);
  for (const c of n.childNodes) walk(c);
  if (wrap) out.push(wrap);
  if (t === 'TD' || t === 'TH') out.push(' | ');
  if (block.has(t)) out.push('\n');
};
walk(root);
const text = out.join('').replace(/[ \t]+\n/g, '\n').replace(/\n(• |\d+\. )\n+/g, '\n$1').replace(/\n{2,}(?=\s*(• |\d+\. ))/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
return {text, streaming: !!document.querySelector(stop)};
