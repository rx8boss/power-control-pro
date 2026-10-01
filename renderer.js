const clickSound = new Audio("assets/click.mp3");
const warnSound = new Audio("assets/warn.mp3");
let countdown = null;

function playClick() { clickSound.currentTime = 0; clickSound.play().catch(() => {}); }
function playWarn() { warnSound.currentTime = 0; warnSound.play().catch(() => {}); }
function escapeHtml(text) { return String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }

function log(msg) {
  const box = document.getElementById("log");
  const line = "[" + new Date().toLocaleTimeString() + "] " + msg;
  if (box) { box.innerHTML += escapeHtml(line) + "<br>"; box.scrollTop = box.scrollHeight; }
  window.api?.saveLog?.(msg);
}
async function loadLogs() {
  const data = (await window.api?.getLogs?.()) || "";
  const box = document.getElementById("log");
  if (box) box.innerHTML = escapeHtml(data).replace(/\n/g, "<br>");
}
async function clearLogs() { await window.api?.clearLogs?.(); loadLogs(); }
function openLogFile() { window.api?.openLogFile?.(); }
function setStatus(text) { const el = document.getElementById("status"); if (el) el.innerText = text; log(text); }

function confirmAction(text) {
  return new Promise((resolve) => {
    const modal = document.getElementById("modal");
    const txt = document.getElementById("modal-text");
    const ok = document.getElementById("modal-ok");
    const cancel = document.getElementById("modal-cancel");
    if (!modal || !txt || !ok || !cancel) return resolve(false);
    txt.innerText = text; modal.classList.remove("hidden");
    const clean = (result) => { modal.classList.add("hidden"); ok.onclick = null; cancel.onclick = null; resolve(result); };
    ok.onclick = () => clean(true); cancel.onclick = () => clean(false);
  });
}

async function shutdown(skip = false) {
  playClick(); if (!skip && !(await confirmAction("PC herunterfahren?"))) return;
  const r = await window.api.run("shutdown -s -t 0"); setStatus(r === "OK" ? "Herunterfahren..." : "Fehler");
}
async function restart(skip = false) {
  playClick(); if (!skip && !(await confirmAction("Neustart?"))) return;
  const r = await window.api.run("shutdown -r -t 0"); setStatus(r === "OK" ? "Neustart..." : "Fehler");
}
async function logout() {
  playClick(); if (!(await confirmAction("Abmelden?"))) return;
  const r = await window.api.run("shutdown -l"); setStatus(r === "OK" ? "Abmelden..." : "Fehler");
}
async function lockWindows() {
  playClick(); if (!(await confirmAction("Windows sperren?"))) return;
  const r = await window.api.run("rundll32.exe user32.dll,LockWorkStation"); setStatus(r === "OK" ? "Windows gesperrt" : "Fehler");
}
function setAbortAktiv(state) { const btn = document.getElementById("btn-abort"); if (btn) btn.disabled = !state; }
async function abort() {
  playClick(); if (countdown) { clearInterval(countdown); countdown = null; }
  const el = document.getElementById("countdown"); if (el) el.innerText = "";
  setAbortAktiv(false); const r = await window.api.run("shutdown -a");
  setStatus(r === "OK" ? "Timer abgebrochen" : "Kein Timer aktiv");
}
function formatDauer(sec) { return Math.floor(sec / 60) + "m " + sec % 60 + "s"; }
function getTimerAuswahl() {
  const sel = document.getElementById("timer-dauer");
  return { sec: parseInt(sel?.value, 10), label: sel?.selectedOptions?.[0]?.text || "" };
}
function startTimer(sec, action, label, type) {
  if (!sec || sec <= 0) return; clearInterval(countdown);
  let t = sec; const el = document.getElementById("countdown");
  setAbortAktiv(true); log("⏱ Timer gestartet: " + type + " in " + label);
  countdown = setInterval(async () => {
    t--; if (el) el.innerText = t > 0 ? "⏳ " + formatDauer(t) : "";
    if (t === 5) { playWarn(); log("⚠ Aktion in 5 Sekunden"); }
    if (t <= 0) { clearInterval(countdown); countdown = null; setAbortAktiv(false); await action(); }
  }, 1000);
}
function shutdownTimer() { const { sec, label } = getTimerAuswahl(); if (!sec) return alert("Zeit wählen"); startTimer(sec, () => shutdown(true), label, "Shutdown"); }
function restartTimer() { const { sec, label } = getTimerAuswahl(); if (!sec) return alert("Zeit wählen"); startTimer(sec, () => restart(true), label, "Restart"); }

function setTheme(file) {
  const link = document.getElementById("theme-style"); if (!link) return;
  link.href = file; localStorage.setItem("theme", file);
  const btn = document.getElementById("theme-toggle-btn");
  if (btn) btn.innerText = file.includes("neon") ? "☀️ Standard" : "🌙 Neon";
}
document.addEventListener("DOMContentLoaded", () => {
  const $ = (id) => document.getElementById(id);
  loadLogs();
  $("btn-shutdown")?.addEventListener("click", () => shutdown());
  $("btn-restart")?.addEventListener("click", () => restart());
  $("btn-logout")?.addEventListener("click", logout);
  $("btn-lock")?.addEventListener("click", lockWindows);
  $("btn-abort")?.addEventListener("click", abort);
  $("btn-shutdown-timer")?.addEventListener("click", shutdownTimer);
  $("btn-restart-timer")?.addEventListener("click", restartTimer);
  $("btn-refresh-log")?.addEventListener("click", loadLogs);
  $("btn-clear-log")?.addEventListener("click", clearLogs);
  $("btn-open-log")?.addEventListener("click", openLogFile);
  const auto = $("autostart");
  if (auto && window.api?.getAutostart) {
    window.api.getAutostart().then((on) => { auto.checked = !!on; });
    auto.addEventListener("change", () => { window.api.toggleAutostart(auto.checked); log(auto.checked ? "Autostart ON" : "Autostart OFF"); });
  }
  setTheme(localStorage.getItem("theme") || "css/style.css");
  $("theme-toggle-btn")?.addEventListener("click", () => {
    const current = localStorage.getItem("theme") || "css/style.css";
    setTheme(current.includes("neon") ? "css/style.css" : "css/neon.css");
  });
});
