/* QASSIMZ Thumbs — viewer UI */
const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const host = window.chrome && window.chrome.webview;

/* ------------------------------------------------------------ bridge */
let seq = 0;
const waiting = new Map();
function call(cmd, extra = {}) {
  if (!host) return demo(cmd, extra);
  const id = ++seq;
  return new Promise((res) => { waiting.set(id, res); host.postMessage({ cmd, id, ...extra }); });
}
if (host) host.addEventListener("message", (e) => {
  const { type, data } = e.data || {};
  if (type === "reply") { const r = waiting.get(data.id); if (r) { waiting.delete(data.id); r(data.data); } }
  else if (type === "update") showUpdate(data);
  else if (type === "updateProgress") { $("#updBar").style.width = data + "%"; $("#btnUpdate").textContent = `بيحمّل… ${data}%`; }
  else if (type === "latest") toast(data ? "✓ عندك أحدث إصدار" : "مقدرتش أوصل لـ GitHub دلوقتي");
  else if (type === "error") toast("⚠️ " + data);
  else if (type === "navigate") go(data);
});

/* ------------------------------------------------------------ kinds */
let INFO = {};
const IMG = new Set([".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff", ".svg", ".ico", ".jxl"]);
const VID = new Set([".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v", ".flv", ".mts", ".m2ts", ".3gp", ".mpg", ".mpeg"]);
const AUD = new Set([".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma"]);
let DESIGN = new Set(), RAW = new Set();
function kind(it) {
  if (it.d) return "folder";
  const e = it.e || "";
  if (DESIGN.has(e)) return "design";
  if (RAW.has(e)) return "raw";
  if (IMG.has(e)) return "image";
  if (VID.has(e)) return "video";
  if (AUD.has(e)) return "audio";
  return "other";
}
const PH = { folder: "📁", design: "🎨", raw: "📷", image: "🖼️", video: "🎬", audio: "🎵", other: "📄" };
const isVisual = (k) => k === "design" || k === "raw" || k === "image" || k === "video";

/* ------------------------------------------------------------ state */
const st = { path: "", items: [], view: [], back: [], filter: "all", sort: "name", q: "", size: 180, lbIndex: -1 };

function bucket(px) { const need = px * (window.devicePixelRatio || 1); return [128, 192, 256, 384, 512].find((b) => b >= need) || 640; }
function thumbUrl(it, s) { return host ? `https://thumb.qassimz/t?p=${encodeURIComponent(it.p)}&s=${s}&m=${it.m}` : demoThumb(it, s); }
const fmtSize = (n) => n == null ? "" : n < 1024 ? n + " B" : n < 1048576 ? (n / 1024).toFixed(0) + " KB" : n < 1073741824 ? (n / 1048576).toFixed(1) + " MB" : (n / 1073741824).toFixed(2) + " GB";
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

/* ------------------------------------------------------------ navigation */
async function go(path, push = true) {
  if (!path) return;
  const r = await call("list", { path });
  if (!r || r.error) { toast("⚠️ " + ((r && r.error) || "مقدرتش أفتح المجلد")); return; }
  if (push && st.path && st.path !== r.path) st.back.push(st.path);
  st.path = r.path; st.items = r.items; st.parent = r.parent;
  $("#btnBack").disabled = !st.back.length;
  $("#btnUp").disabled = !r.parent;
  renderCrumbs(r.path);
  renderRecent(r.recent || []);
  markActive();
  $("#gridWrap").scrollTop = 0;
  render();
}

function renderCrumbs(p) {
  const parts = p.replace(/\\+$/, "").split("\\");
  let acc = "";
  $("#crumbs").innerHTML = parts.map((x, i) => {
    acc += (i ? "\\" : "") + x;
    const full = i === 0 ? x + "\\" : acc;
    return (i ? '<span class="sep">›</span>' : "") + `<button data-p="${esc(full)}">${esc(x)}</button>`;
  }).join("");
  $$("#crumbs button").forEach((b) => (b.onclick = () => go(b.dataset.p)));
  const c = $("#crumbs"); c.scrollLeft = c.scrollWidth;
}

function navItem(p) { return `<button class="nav" data-p="${esc(p.path)}" title="${esc(p.path)}"><i class="ic">${{ desktop: "🖥️", downloads: "⬇️", pictures: "🖼️", documents: "📄", videos: "🎬", drive: "💽", recent: "🕘" }[p.icon] || "📁"}</i><span>${esc(p.name)}</span></button>`; }
function renderPlaces() {
  $("#places").innerHTML = INFO.places.map(navItem).join("");
  $("#drives").innerHTML = INFO.drives.map(navItem).join("");
  $$(".side .nav").forEach((b) => (b.onclick = () => go(b.dataset.p)));
}
function renderRecent(list) {
  const known = new Set([...INFO.places, ...INFO.drives].map((p) => p.path.toLowerCase()));
  const r = list.filter((p) => !known.has(p.toLowerCase()) && p.toLowerCase() !== st.path.toLowerCase()).slice(0, 6);
  $("#recentH").classList.toggle("hidden", !r.length);
  $("#recent").innerHTML = r.map((p) => navItem({ path: p, name: p.split("\\").filter(Boolean).pop(), icon: "recent" })).join("");
  $$("#recent .nav").forEach((b) => (b.onclick = () => go(b.dataset.p)));
}
function markActive() { $$(".side .nav").forEach((b) => b.classList.toggle("active", b.dataset.p.toLowerCase() === st.path.toLowerCase())); }

/* ------------------------------------------------------------ grid */
let io;
function render() {
  const q = st.q.toLowerCase();
  let v = st.items.filter((it) => {
    const k = kind(it);
    if (q && !it.n.toLowerCase().includes(q)) return false;
    if (st.filter === "all") return true;
    return k === "folder" ? false : k === st.filter;
  });
  const cmp = {
    name: (a, b) => a.n.localeCompare(b.n, undefined, { numeric: true, sensitivity: "base" }),
    date: (a, b) => b.m - a.m,
    size: (a, b) => (b.s || 0) - (a.s || 0),
    type: (a, b) => (a.e || "").localeCompare(b.e || "") || a.n.localeCompare(b.n, undefined, { numeric: true }),
  }[st.sort];
  v.sort((a, b) => (b.d ? 1 : 0) - (a.d ? 1 : 0) || cmp(a, b));
  st.view = v;

  const files = v.filter((x) => !x.d).length, folders = v.length - files;
  $("#count").textContent = v.length ? `${files} ملف${folders ? ` · ${folders} مجلد` : ""}` : "";
  $("#empty").classList.toggle("hidden", !!v.length || !!st.path && st.items.length > 0);
  if (!v.length && st.path) { $("#empty").classList.remove("hidden"); $("#empty h2").textContent = st.items.length ? "مفيش نتايج" : "المجلد فاضي"; }

  const s = bucket(st.size);
  $("#grid").style.setProperty("--t", st.size + "px");
  $("#grid").innerHTML = v.map((it, i) => {
    const k = kind(it);
    const ext = (it.e || "").slice(1).toUpperCase();
    return `<div class="tile ${k === "folder" ? "folder" : ""}" data-i="${i}" title="${esc(it.n)}${it.s != null ? "\n" + fmtSize(it.s) : ""}">
      <div class="thumb ${k !== "folder" ? "shim" : ""}">${k !== "folder" ? `<img data-src="${esc(thumbUrl(it, s))}" alt="">` : ""}<span class="ph">${PH[k]}</span>
      ${ext && k !== "folder" ? `<span class="badge ${k}">${esc(ext)}</span>` : ""}</div>
      <div class="nm">${esc(it.n)}</div></div>`;
  }).join("");

  io && io.disconnect();
  io = new IntersectionObserver((ents) => ents.forEach((en) => {
    if (!en.isIntersecting) return;
    const img = en.target; io.unobserve(img);
    img.onload = () => { img.classList.add("ok"); img.parentElement.classList.remove("shim"); img.nextElementSibling.classList.add("gone"); };
    img.onerror = () => { img.parentElement.classList.remove("shim"); img.remove(); };
    img.src = img.dataset.src;
  }), { root: $("#gridWrap"), rootMargin: "600px" });
  $$("#grid img").forEach((im) => io.observe(im));

  $$("#grid .tile").forEach((t) => {
    t.onclick = (e) => { $$(".tile.sel").forEach((x) => x.classList.remove("sel")); t.classList.add("sel"); };
    t.ondblclick = () => openItem(+t.dataset.i);
  });
}

function openItem(i) {
  const it = st.view[i];
  if (!it) return;
  if (it.d) return go(it.p);
  const k = kind(it);
  if (isVisual(k) && k !== "video") openLb(i);
  else call("open", { path: it.p });
}

/* ------------------------------------------------------------ lightbox */
let zoom = 1, panX = 0, panY = 0;
function visualIdx() { return st.view.map((x, i) => [x, i]).filter(([x]) => !x.d && isVisual(kind(x)) && kind(x) !== "video").map(([, i]) => i); }

async function openLb(i) {
  st.lbIndex = i;
  const it = st.view[i];
  const k = kind(it);
  $("#lb").classList.remove("hidden");
  resetZoom();
  const img = $("#lbImg");
  const small = thumbUrl(it, bucket(st.size));
  img.style.opacity = ".6"; img.src = small; $("#lbSpin").classList.remove("hidden");
  const big = thumbUrl(it, Math.min(4096, Math.round(Math.max(innerWidth, innerHeight) * (devicePixelRatio || 1))));
  const pre = new Image();
  pre.onload = () => { if (st.lbIndex === i) { img.src = big; img.style.opacity = "1"; $("#lbSpin").classList.add("hidden"); } };
  pre.onerror = () => { if (st.lbIndex === i) { img.style.opacity = "1"; $("#lbSpin").classList.add("hidden"); } };
  pre.src = big;

  $("#lbName").textContent = it.n;
  const b = $("#lbBadge"); b.textContent = (it.e || "").slice(1).toUpperCase(); b.className = "badge big " + k;
  $("#lbMeta").innerHTML = `<dt>الحجم</dt><dd>${fmtSize(it.s)}</dd>`;
  const d = await call("details", { path: it.p });
  if (st.lbIndex !== i || !d) return;
  const rows = [];
  if (d.width) rows.push(["الأبعاد", `${d.width} × ${d.height} px`]);
  if (d.mode) rows.push(["الألوان", `${d.mode} · ${d.depth}-bit`]);
  rows.push(["الحجم", fmtSize(d.size)], ["آخر تعديل", d.modified], ["اتعمل", d.created], ["المسار", d.path]);
  $("#lbMeta").innerHTML = rows.map(([a, v]) => `<dt>${a}</dt><dd>${esc(v)}</dd>`).join("");
}
function lbStep(dir) {
  const idx = visualIdx(); if (!idx.length) return;
  const pos = idx.indexOf(st.lbIndex);
  openLb(idx[(pos + dir + idx.length) % idx.length]);
}
function closeLb() { $("#lb").classList.add("hidden"); st.lbIndex = -1; }
function resetZoom() { zoom = 1; panX = panY = 0; applyZoom(); }
function applyZoom() { const im = $("#lbImg"); im.style.transform = `translate(${panX}px,${panY}px) scale(${zoom})`; im.classList.toggle("zoomed", zoom > 1); }

/* ------------------------------------------------------------ settings / update */
function showUpdate(u) {
  INFO.update = u;
  $("#updPill").classList.remove("hidden"); $("#updVer").textContent = "v" + u.version;
  $("#updBox").classList.remove("hidden"); $("#updVer2").textContent = "v" + u.version;
}
function setXp(on, raw) {
  INFO.explorer = on; INFO.raw = raw;
  $("#s_xp").checked = on; $("#s_raw").checked = raw; $("#s_raw").disabled = !on;
  $(".led").classList.toggle("on", on);
  $("#xpText").textContent = on ? "مصغّرات الويندوز شغّالة" : "مصغّرات الويندوز مقفولة";
}
function saveSettings() { call("settings", { value: { thumbSize: st.size, sort: st.sort, filter: st.filter, autoUpdate: $("#s_upd").checked } }); }

/* ------------------------------------------------------------ toast */
let tT;
function toast(msg, acts = []) {
  $("#toastMsg").textContent = msg; $("#toastAct").innerHTML = "";
  acts.forEach(([t, fn]) => { const b = document.createElement("button"); b.className = "btn ghost"; b.textContent = t; b.onclick = () => { fn(); $("#toast").classList.add("hidden"); }; $("#toastAct").appendChild(b); });
  $("#toast").classList.remove("hidden"); clearTimeout(tT); tT = setTimeout(() => $("#toast").classList.add("hidden"), acts.length ? 9000 : 3500);
}

/* ------------------------------------------------------------ init */
async function init() {
  INFO = await call("init");
  DESIGN = new Set(INFO.designExt); RAW = new Set(INFO.rawExt);
  $("#verText").textContent = "الإصدار " + INFO.version;
  const s = INFO.settings || {};
  st.size = s.thumbSize || 180; st.sort = s.sort || "name"; st.filter = s.filter || "all";
  $("#sizeRange").value = st.size; $("#sortSel").value = st.sort;
  $$("#filterSeg button").forEach((b) => b.classList.toggle("on", b.dataset.v === st.filter));
  $("#s_upd").checked = s.autoUpdate !== false;
  $("#cacheSize").textContent = fmtSize(INFO.cache || 0);
  setXp(INFO.explorer, INFO.raw);
  if (INFO.update) showUpdate(INFO.update);
  renderPlaces();
  if (!localStorageGet("welcomed")) $("#welcome").classList.remove("hidden");
  await go(INFO.start, false);
}
function localStorageGet(k) { try { return localStorage.getItem(k); } catch (e) { return "1"; } }
function localStorageSet(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }

/* wiring */
$("#btnOpen").onclick = async () => { const p = await call("pick", { path: st.path }); if (p) go(p); };
$("#btnBack").onclick = () => { const p = st.back.pop(); if (p) go(p, false); };
$("#btnUp").onclick = () => st.parent && go(st.parent);

let searchTimer;
$("#search").oninput = (e) => {
  const val = e.target.value;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => { st.q = val; render(); }, 120);
};

/* integrated frameless window controls & drag */
$("#winMin").onclick = () => call("win_min");
$("#winMax").onclick = () => call("win_max");
$("#winClose").onclick = () => call("win_close");
const bindWindowDrag = (el) => {
  if (!el) return;
  el.addEventListener("mousedown", (e) => {
    if (e.button !== 0 || e.detail > 1) return;
    if (e.target.closest("button, input, select, a, .win-controls, .crumbs button")) return;
    if (host) host.postMessage({ cmd: "win_drag", id: 0 });
  });
  el.addEventListener("dblclick", (e) => {
    if (e.target.closest("button, input, select, a, .win-controls, .crumbs button")) return;
    call("win_max");
  });
};
bindWindowDrag($("#topToolbar"));
bindWindowDrag($("#brandDrag"));

$$("#filterSeg button").forEach((b) => (b.onclick = () => { st.filter = b.dataset.v; $$("#filterSeg button").forEach((x) => x.classList.toggle("on", x === b)); render(); saveSettings(); }));
$("#sortSel").onchange = (e) => { st.sort = e.target.value; render(); saveSettings(); };
let rT; $("#sizeRange").oninput = (e) => { st.size = +e.target.value; $("#grid").style.setProperty("--t", st.size + "px"); clearTimeout(rT); rT = setTimeout(() => { render(); saveSettings(); }, 350); };

$("#lbClose").onclick = closeLb;
$("#lbPrev").onclick = () => lbStep(-1);
$("#lbNext").onclick = () => lbStep(1);
const cur = () => st.view[st.lbIndex];
$("#lbOpen").onclick = () => call("open", { path: cur().p });
$("#lbReveal").onclick = () => call("reveal", { path: cur().p });
$("#lbWith").onclick = () => call("openWith", { path: cur().p });
$("#lbCopy").onclick = async () => toast((await call("copy", { path: cur().p })) ? "✓ الصورة اتنسخت" : "مقدرتش أنسخ الصورة");
$("#lbExport").onclick = async () => { const r = await call("export", { path: cur().p }); if (r) toast("✓ اتحفظت", [["اعرضها", () => call("reveal", { path: r })]]); };
$("#lbStage").addEventListener("wheel", (e) => { e.preventDefault(); zoom = Math.min(8, Math.max(1, zoom * (e.deltaY < 0 ? 1.15 : 1 / 1.15))); if (zoom === 1) panX = panY = 0; applyZoom(); }, { passive: false });
$("#lbImg").ondblclick = () => { zoom = zoom > 1 ? 1 : 2.5; if (zoom === 1) panX = panY = 0; applyZoom(); };
let drag = null;
$("#lbImg").onpointerdown = (e) => { if (zoom <= 1) return; drag = { x: e.clientX - panX, y: e.clientY - panY }; e.target.setPointerCapture(e.pointerId); };
$("#lbImg").onpointermove = (e) => { if (!drag) return; panX = e.clientX - drag.x; panY = e.clientY - drag.y; applyZoom(); };
$("#lbImg").onpointerup = () => (drag = null);

document.addEventListener("keydown", (e) => {
  const lb = !$("#lb").classList.contains("hidden");
  const active = document.activeElement;
  const tag = active ? active.tagName : "";
  const typing = tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || !!(active && active.isContentEditable);
  const mod = e.ctrlKey || e.metaKey;

  if (e.code === "Escape") {
    if (lb) { e.preventDefault(); closeLb(); return; }
    const openModals = $$(".modal:not(.hidden)");
    if (openModals.length) { e.preventDefault(); openModals.forEach((m) => m.classList.add("hidden")); return; }
    if (active === $("#search") || st.q) {
      e.preventDefault();
      if (st.q) { st.q = ""; $("#search").value = ""; render(); }
      $("#search").blur();
      return;
    }
    return;
  }
  if (mod && !e.altKey && e.code === "KeyF") {
    e.preventDefault();
    $("#search").focus();
    $("#search").select();
    return;
  }
  if (mod && !e.altKey && e.code === "KeyO") {
    e.preventDefault();
    $("#btnOpen").click();
    return;
  }
  if (mod && !e.altKey && e.code === "Comma") {
    e.preventDefault();
    $("#settings").classList.remove("hidden");
    return;
  }
  if (lb) {
    if (e.code === "ArrowLeft") { e.preventDefault(); lbStep(1); return; }
    if (e.code === "ArrowRight") { e.preventDefault(); lbStep(-1); return; }
    if (e.code === "Digit0" || e.code === "Numpad0") { e.preventDefault(); resetZoom(); return; }
    return;
  }
  if (typing) return;
  if (!mod && !e.altKey && !e.shiftKey && e.code === "Slash") {
    e.preventDefault();
    $("#search").focus();
    $("#search").select();
    return;
  }
  if ((e.code === "Enter" || e.code === "NumpadEnter" || e.code === "Space") && tag !== "BUTTON") {
    const s = $(".tile.sel");
    if (s) { e.preventDefault(); openItem(+s.dataset.i); }
    return;
  }
  if (e.code === "Backspace") {
    if (st.parent) { e.preventDefault(); go(st.parent); }
  }
});
document.addEventListener("mouseup", (e) => { if (e.button === 3) $("#btnBack").click(); });

/* settings */
$("#btnSettings").onclick = $("#xpPill").onclick = () => $("#settings").classList.remove("hidden");
$("#updPill").onclick = () => $("#settings").classList.remove("hidden");
$("#setClose").onclick = () => $("#settings").classList.add("hidden");
$("#s_xp").onchange = async (e) => { const r = await call("explorer", { on: e.target.checked, raw: $("#s_raw").checked }); if (r) { setXp(r.on, r.raw); toast(r.on ? "✓ مصغّرات الويندوز اشتغلت" : "اتقفلت"); } };
$("#s_raw").onchange = async (e) => { const r = await call("explorer", { on: true, raw: e.target.checked }); if (r) setXp(r.on, r.raw); };
$("#s_upd").onchange = saveSettings;
$("#btnRefreshXp").onclick = async () => { await call("refreshExplorer"); toast("✓ اتحدّثت — افتح المجلد تاني في File Explorer"); };
$("#btnClearCache").onclick = async () => { const n = await call("clearCache"); $("#cacheSize").textContent = fmtSize(n || 0); toast("✓ الكاش اتمسح"); };
$("#btnCheckUpd").onclick = () => call("checkUpdate");
$("#btnUpdate").onclick = () => { $("#btnUpdate").disabled = true; call("installUpdate"); };
[["#btnGithub", "github"], ["#btnGithub2", "github"], ["#btnSupport", "support"], ["#btnSupport2", "support"], ["#btnSite", "site"]].forEach(([s, k]) => ($(s).onclick = () => call("url", { url: INFO[k] })));
$("#btnWelcome").onclick = () => { $("#welcome").classList.add("hidden"); localStorageSet("welcomed", "1"); };

/* drag & drop a folder */
let dragN = 0;
addEventListener("dragenter", (e) => { e.preventDefault(); if (++dragN === 1) $("#drop").classList.remove("hidden"); });
addEventListener("dragleave", () => { if (--dragN <= 0) { dragN = 0; $("#drop").classList.add("hidden"); } });
addEventListener("dragover", (e) => e.preventDefault());
addEventListener("drop", (e) => {
  e.preventDefault(); dragN = 0; $("#drop").classList.add("hidden");
  const files = [...(e.dataTransfer.files || [])];
  if (files.length && host && host.postMessageWithAdditionalObjects) host.postMessageWithAdditionalObjects({ cmd: "drop", id: 0 }, files);
});

/* ------------------------------------------------------------ browser demo (design preview only) */
function demo(cmd, x) {
  const P = "C:\\Users\\Ahmed\\Pictures\\QASSIMZ";
  const names = ["thumbnail-ep12.psd", "logo-qassimz.ai", "intro-title.mogrt", "poster-final.psd", "DSC_0421.NEF", "IMG_2210.CR3", "banner.xd", "brand-kit.eps", "frame-07.png", "cover.jpg", "teaser.mp4", "sketch.kra", "story-board.afdesign", "studio.jpg", "lower-third.mogrt", "episode-13.psd", "B-roll.mov", "font-test.png"];
  const R = {
    init: { version: "1.1.0", github: "#", support: "#", site: "#", places: [{ name: "سطح المكتب", path: "C:\\Users\\Ahmed\\Desktop", icon: "desktop" }, { name: "التنزيلات", path: "C:\\Users\\Ahmed\\Downloads", icon: "downloads" }, { name: "الصور", path: "C:\\Users\\Ahmed\\Pictures", icon: "pictures" }, { name: "المستندات", path: "C:\\Users\\Ahmed\\Documents", icon: "documents" }, { name: "الفيديوهات", path: "C:\\Users\\Ahmed\\Videos", icon: "videos" }], drives: [{ name: "C:", path: "C:\\", icon: "drive" }, { name: "Projects (D:)", path: "D:\\", icon: "drive" }], start: P, explorer: true, raw: false, cache: 18400000, settings: {}, designExt: [".psd", ".ai", ".eps", ".xd", ".mogrt", ".kra", ".afdesign"], rawExt: [".nef", ".cr3"] },
    list: { path: P, parent: "C:\\Users\\Ahmed\\Pictures", recent: ["D:\\Projects\\Ep 12"], items: [{ n: "Exports", p: P + "\\Exports", d: true, m: 1 }, { n: "Raw footage", p: P + "\\Raw", d: true, m: 1 }, ...names.map((n, i) => ({ n, p: P + "\\" + n, s: 1e6 * (i + 3) * 7, m: 1000 - i, e: n.slice(n.lastIndexOf(".")).toLowerCase() }))] },
    details: { width: 3840, height: 2160, mode: "RGB", depth: 8, size: 48200000, modified: "2026-11-24 22:10", created: "2026-11-20 14:02", path: P + "\\thumbnail-ep12.psd" },
  };
  return Promise.resolve(R[cmd] ?? null);
}
const demoCache = {};
function demoThumb(it, s) {
  const key = it.n + s; if (demoCache[key]) return demoCache[key];
  const c = document.createElement("canvas"); const h0 = [...it.n].reduce((a, ch) => a + ch.charCodeAt(0), 0);
  const portrait = h0 % 3 === 0, w = portrait ? s * 0.7 : s, h = portrait ? s : s * 0.5625 * (h0 % 2 ? 1 : 1.3);
  c.width = w; c.height = h; const g = c.getContext("2d");
  const pal = [["#0a84ff", "#5ff0ff"], ["#081a38", "#2bb5ff"], ["#1e3a8a", "#32d4ff"], ["#040a14", "#0a84ff"], ["#3fa9ff", "#d8f6ff"]][h0 % 5];
  const gr = g.createLinearGradient(0, 0, w, h); gr.addColorStop(0, pal[0]); gr.addColorStop(1, pal[1]); g.fillStyle = gr; g.fillRect(0, 0, w, h);
  g.globalAlpha = .35; g.fillStyle = "#fff"; g.beginPath(); g.arc(w * (0.2 + (h0 % 7) / 10), h * .4, Math.min(w, h) * .28, 0, 7); g.fill();
  g.globalAlpha = .9; g.fillStyle = "#04132b"; g.font = `800 ${Math.round(h * .16)}px 'Google Sans Flex', 'Google Sans', sans-serif`; g.textAlign = "center"; g.fillText(["قاسمز", "الحلقة ١٢", "QASSIMZ", "كواليس", "إنترو"][h0 % 5], w / 2, h * .78);
  return (demoCache[key] = c.toDataURL());
}

document.fonts.ready.then(init);
