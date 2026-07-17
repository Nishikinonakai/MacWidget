// 宿主注入运行时（AddScriptToExecuteOnDocumentCreated 注入，组件作者零感知）：
// ① 状态类驱动（dark/mono/editing/bye）② 编辑态减号徽章 ③ 右键=编辑模式 ④ 拖拽摆位上报。
// 注入前宿主会先注入 window.__mwInit = {dark,mono,editing} 快照，导航完成后再补推一次兜住竞态。
(function () {
  if (window.__mwHost) return; window.__mwHost = true;
  const H = document.documentElement;

  function apply(s) {
    if ('dark' in s) H.classList.toggle('dark', !!s.dark);
    if ('mono' in s) H.classList.toggle('mono', !!s.mono);
    if ('editing' in s) H.classList.toggle('editing', !!s.editing);
  }
  apply(window.__mwInit || {});

  window.chrome?.webview?.addEventListener('message', e => {
    const m = e.data || {};
    if (m.t === 'state') apply(m);
    else if (m.t === 'bye') H.classList.add('bye');
  });

  const post = m => window.chrome?.webview?.postMessage(m);

  // 减号徽章（样式在 widget.css，仅编辑态可见/可点）
  addEventListener('DOMContentLoaded', () => {
    const b = document.createElement('div');
    b.className = 'mw-badge';
    b.addEventListener('click', () => post({ t: 'remove' }));
    document.body.appendChild(b);
  });

  // 右键 = 编辑模式开关（macOS 为菜单项 Edit Widgets…，MVP 直达）
  addEventListener('contextmenu', e => { e.preventDefault(); post({ t: 'edit' }); }, true);

  // 拖拽摆位（原型验证过的机制原样保留）
  let sx = 0, sy = 0, armed = false, dragging = false;
  addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    armed = true; dragging = false; sx = e.screenX; sy = e.screenY;
  }, true);
  addEventListener('pointermove', e => {
    if (!armed) return;
    const dx = e.screenX - sx, dy = e.screenY - sy;
    if (!dragging && Math.hypot(dx, dy) > 4) { dragging = true; post({ t: 'dragstart' }); }
    if (dragging) post({ t: 'drag', dx: dx, dy: dy });
  }, true);
  const end = () => {
    if (dragging) post({ t: 'dragend' });
    armed = false; dragging = false;
  };
  addEventListener('pointerup', end, true);
  addEventListener('pointercancel', end, true);
})();
