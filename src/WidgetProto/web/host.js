// 宿主注入运行时（AddScriptToExecuteOnDocumentCreated 注入，组件作者零感知）：
// ① 状态类驱动（dark/mono/editing/bye）② 编辑态减号徽章 ③ 右键=组件菜单 ④ 拖拽摆位上报
// ⑤ 数据桥 mw.subscribe。
// 注入前宿主会先注入 window.__mwInit = {dark,mono,editing} 快照，导航完成后再补推一次兜住竞态。
// ⚠️ document-created 时机 documentElement 可能尚未存在——一切 DOM 访问都要懒取+兜底，
//    任何顶层异常都会杀死整个运行时（含拖拽），所以 try 包住非关键段。
(function () {
  if (window.__mwHost) return; window.__mwHost = true;

  const post = m => { try { window.chrome?.webview?.postMessage(m); } catch { } };
  const english = window.__mwInit?.lang === 'en';
  // One shared runtime language bundle keeps independently authored widget
  // pages consistent. It also observes dynamic data labels, so providers do
  // not need to duplicate localization plumbing for every update.
  const enText = {
    '搜索组件':'Search widgets','浏览':'Browse','组件分类':'Widget categories','全部组件':'All Widgets','日程':'Schedule','工具':'Tools','信息':'Information','媒体':'Media',
    '精选推荐':'Featured','根据当前桌面':'Based on your desktop','没有符合条件的组件。':'No matching widgets.','将组件拖到桌面上的任意位置来放置…':'Drag a widget anywhere on the desktop.','完成':'Done',
    '时钟':'Clock','时间与世界时钟':'Time and world clocks','日历':'Calendar','日期与月历':'Date and month view','系统监视':'System Monitor','CPU、内存与磁盘':'CPU, memory, and disk',
    '专注计时器':'Focus Timer','倒计时与专注提醒':'Countdown and focus reminder','本地速记':'Local Note','一张只保存在本机的便签':'A note stored only on this device',
    '防休眠':'Keep Awake','定时阻止系统进入睡眠':'Temporarily prevent system sleep','离线二维码':'Offline QR','把网址或文字传给手机':'Send a URL or text to your phone',
    '计算器':'Calculator','随手计算，不离开桌面':'Quick calculations on your desktop','快捷网址':'Quick Links','自己的常用网址面板':'Your personal website launcher',
    '电池':'Battery','电量与充电状态':'Battery and charging','正在播放':'Now Playing','媒体播放控制':'Media controls','天气':'Weather','当前天气与预报':'Current conditions and forecast','照片':'Photos','文件夹照片轮播':'Photo-folder slideshow',
    '搜索结果：':'Search results: ','个组件':' widgets','加载中':'Loading','天气地点':'Weather location','输入城市或地区':'Enter a city or region','搜索':'Search','搜索：Open-Meteo · 天气：MET Norway':'Search: Open-Meteo · Weather: MET Norway',
    '星期日':'Sunday','星期一':'Monday','星期二':'Tuesday','星期三':'Wednesday','星期四':'Thursday','星期五':'Friday','星期六':'Saturday',
    '杭州':'Hangzhou','东京':'Tokyo','伦敦':'London','纽约':'New York',
    '暂时无法获取天气':'Weather is temporarily unavailable','请输入至少两个字符':'Enter at least two characters','正在搜索…':'Searching…','搜索服务暂不可用':'Search is unavailable','未找到地点':'No locations found',
    '右键 → 编辑「照片」选择文件夹':'Right-click → Edit “Photos” to choose a folder','文件夹':'Folder','（图片）':'(Pictures)','选择…':'Choose…','轮换间隔':'Rotation interval','秒':' sec',
    '没有播放内容':'Nothing playing','未知曲目':'Unknown track','内存':'Memory','磁盘':'Disk','无电池':'No battery','充电中':'Charging','已充满':'Fully charged','剩余 ':'Remaining ',
    '右键 → 编辑「快捷网址」来添加':'Right-click → Edit “Quick Links” to add sites','名称':'Name','网址':'URL','例如：文档':'e.g. Docs','例如：example.com':'e.g. example.com','清除':'Clear',
    '专注':'Focus','准备开始':'Ready','进行中':'Running','已暂停':'Paused','已完成':'Complete','开始':'Start','暂停':'Pause','复位':'Reset','分钟':'min',
    '右键 → 编辑「本地速记」开始记录':'Right-click → Edit “Local Note” to start writing','标题（可选）':'Title (optional)','在这里写点什么…':'Write something…','内容只保存在这台电脑上':'Stored only on this device'
  };
  const enPairs = Object.entries(enText).sort((a, b) => b[0].length - a[0].length);
  function localize(root) {
    if (!english || !root) return;
    const translate = s => enPairs.reduce((v, [zh, en]) => v.split(zh).join(en), s);
    const walk = node => {
      if (node.nodeType === Node.TEXT_NODE) {
        if (node.parentElement?.closest('[data-mw-no-localize]')) return;
        const next = translate(node.nodeValue);
        if (next !== node.nodeValue) node.nodeValue = next;
      }
      else if (node.nodeType === Node.ELEMENT_NODE) {
        // Widget-authored UI is localized, user-authored note/link text is not.
        // Never rewrite content or accessible labels below this boundary.
        if (node.hasAttribute('data-mw-no-localize')) return;
        for (const a of ['placeholder', 'aria-label', 'title']) if (node.hasAttribute(a)) node.setAttribute(a, translate(node.getAttribute(a)));
        node.childNodes.forEach(walk);
      }
    };
    walk(root);
  }

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
    cfg() { return cfg; },                             // 当前实例配置（宿主持久化在布局文件）
    lang() { return (window.__mwInit && window.__mwInit.lang) || 'zh'; },
    saveCfg(c) { cfg = c; post({ t: 'cfg', cfg: c }); },
    openUrl(url) { post({ t: 'openUrl', url: String(url || '') }); },
    makeQr(text) { post({ t: 'qr', text: String(text || '') }); },
    pickFolder(fn) { pickQ.push(fn); post({ t: 'pickfolder' }); },   // 原生选文件夹，fn(path|null)
    exitCfg() {
      try { document.documentElement.classList.remove('cfgmode'); } catch { }
      post({ t: 'cfgdone' });
    },
    on(type, fn) { (evs[type] || (evs[type] = [])).push(fn); },      // 宿主专发消息（如照片清单）
    log(m) { post({ t: 'dbg', m: String(m) }); },                    // 排障：写进宿主 proto.log
  };

  let pending = null;
  function apply(s) {
    const H = document.documentElement;
    if (!H) { pending = Object.assign(pending || {}, s); return; }
    H.classList.add('mw-hosted');
    H.lang = english ? 'en' : 'zh-CN';
    if ('dark' in s) H.classList.toggle('dark', !!s.dark);
    if ('mono' in s) H.classList.toggle('mono', !!s.mono);
    if ('effects' in s) H.classList.toggle('noeffects', !s.effects);
    if ('editing' in s) H.classList.toggle('editing', !!s.editing);
    if ('bye' in s) H.classList.add('bye');
  }
  try { apply(window.__mwInit || {}); } catch { }

  try {
    window.chrome?.webview?.addEventListener('message', e => {
      const m = e.data || {};
      if (m.t === 'state') apply(m);
      else if (m.t === 'backdrop') {
        const H = document.documentElement;
        if (H) {
          H.style.setProperty('--backdrop-image', `url("${m.url}")`);
          H.style.setProperty('--backdrop-size', `${m.width}px ${m.height}px`);
          H.style.setProperty('--backdrop-position', `${m.x}px ${m.y}px`);
        }
      }
      else if (m.t === 'bye') apply({ bye: 1 });
      else if (m.t === 'data') (subs[m.topic] || []).forEach(fn => { try { fn(m); } catch { } });
      else if (m.t === 'editcfg') {
        try {
          document.documentElement.classList.add('cfgmode');
          setTimeout(() => document.querySelector('input,textarea,select,button')?.focus({ preventScroll:true }), 280);
        } catch { }
      }
      else if (m.t === 'folder') { const fn = pickQ.shift(); if (fn) try { fn(m.path || null); } catch { } }
      else (evs[m.t] || []).forEach(fn => { try { fn(m); } catch { } });
    });
  } catch { }

  addEventListener('DOMContentLoaded', () => {
    try {
      if (pending) { const p = pending; pending = null; apply(p); }
      localize(document.body);
      if (english) new MutationObserver(records => records.forEach(r => {
        if (r.type === 'characterData') localize(r.target);
        else r.addedNodes.forEach(localize);
      }))
        .observe(document.body, { childList: true, subtree: true, characterData: true });
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
  addEventListener('keydown', e => {
    if (e.key !== 'Escape' || !document.documentElement?.classList.contains('cfgmode')) return;
    e.preventDefault();
    e.stopPropagation();
    window.mw?.exitCfg();
  }, true);

  // 拖拽摆位（原型验证过的机制原样保留）
  let sx = 0, sy = 0, armed = false, dragging = false;
  addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    if (e.target.closest?.('input, textarea, select, button, a, [contenteditable="true"]')) return;
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
