// 宿主注入运行时（AddScriptToExecuteOnDocumentCreated 注入，组件作者零感知）：
// ① 状态类驱动（dark/mono/editing/bye）② 编辑态减号徽章 ③ 右键=组件菜单 ④ 拖拽摆位上报
// ⑤ 数据桥 mw.subscribe。
// 注入前宿主会先注入 window.__mwInit = {dark,mono,editing} 快照，导航完成后再补推一次兜住竞态。
// ⚠️ document-created 时机 documentElement 可能尚未存在——一切 DOM 访问都要懒取+兜底，
//    任何顶层异常都会杀死整个运行时（含拖拽），所以 try 包住非关键段。
(function () {
  if (window.__mwHost) return; window.__mwHost = true;

  const post = m => { try { window.chrome?.webview?.postMessage(m); } catch { } };

  // 数据桥：组件页 mw.subscribe(topic, fn) 订阅宿主数据源。信封
  // {status:'ok'|'loading'|'error', stale, ts, data, error}——error 时 data 是最后一份好数据（可能 null）。
  // 页面每次导航重新执行到这里，重发 sub 即拿到快照回放，无需自己缓存。
  const subs = Object.create(null), evs = Object.create(null), pickQ = [];
  let cfg = (window.__mwInit && window.__mwInit.cfg) || null;
  window.mw = {
    subscribe(topic, fn) {
      (subs[topic] || (subs[topic] = [])).push(fn);
      post({ t: 'sub', topic: topic });
    },
    unsubscribe(topic) {   // 参数化 topic 运行期换挡用（如天气换城市），宿主停掉无人订的采样
      delete subs[topic];
      post({ t: 'unsub', topic: topic });
    },
    send(topic, cmd) {   // 反向通道：播控等命令 → 宿主 provider（ICommandSink）
      post({ t: 'cmd', topic: topic, cmd: cmd });
    },
    // ---- 组件设置流（编辑小组件翻面）----
    cfg() { return cfg; },                             // 当前实例配置（宿主持久化在 widgets.json）
    saveCfg(c) { cfg = c; post({ t: 'cfg', cfg: c }); },
    pickFolder(fn) { pickQ.push(fn); post({ t: 'pickfolder' }); },   // 原生选文件夹，fn(path|null)
    exitCfg() { try { document.documentElement.classList.remove('cfgmode'); } catch { } },
    on(type, fn) { (evs[type] || (evs[type] = [])).push(fn); },      // 宿主专发消息（如照片清单）
    log(m) { post({ t: 'dbg', m: String(m) }); },                    // 排障：写进宿主 proto.log
  };

  let pending = null;
  function apply(s) {
    const H = document.documentElement;
    if (!H) { pending = Object.assign(pending || {}, s); return; }
    if ('dark' in s) H.classList.toggle('dark', !!s.dark);
    if ('mono' in s) H.classList.toggle('mono', !!s.mono);
    if ('editing' in s) H.classList.toggle('editing', !!s.editing);
    if ('bye' in s) H.classList.add('bye');
  }
  try { apply(window.__mwInit || {}); } catch { }

  try {
    window.chrome?.webview?.addEventListener('message', e => {
      const m = e.data || {};
      if (m.t === 'state') apply(m);
      else if (m.t === 'bye') apply({ bye: 1 });
      else if (m.t === 'data') (subs[m.topic] || []).forEach(fn => { try { fn(m); } catch { } });
      else if (m.t === 'editcfg') { try { document.documentElement.classList.add('cfgmode'); } catch { } }
      else if (m.t === 'folder') { const fn = pickQ.shift(); if (fn) try { fn(m.path || null); } catch { } }
      else (evs[m.t] || []).forEach(fn => { try { fn(m); } catch { } });
    });
  } catch { }

  addEventListener('DOMContentLoaded', () => {
    try {
      if (pending) { const p = pending; pending = null; apply(p); }
      // 减号徽章（样式在 widget.css，仅编辑态可见/可点）
      const b = document.createElement('div');
      b.className = 'mw-badge';
      b.addEventListener('click', () => post({ t: 'remove' }));
      document.body.appendChild(b);
    } catch { }
  });

  // 右键 = macOS 式组件菜单（尺寸档/编辑小组件…/移除）；screenX/Y 是 DIP，与宿主 DIU 同标度
  addEventListener('contextmenu', e => {
    e.preventDefault();
    post({ t: 'menu', x: e.screenX, y: e.screenY });
  }, true);

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

  post({ t: 'hello' });   // 注入存活探针（宿主记日志，排"运行时整体哑火"类故障）
})();
