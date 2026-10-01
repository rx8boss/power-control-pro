const api = window.powerControl;
let timerId = null;
let activeTimer = null;
let timerStarting = false;
let timerDeadline = 0;
let audioContext = null;
const soundAssets = {
  warning: Object.fromEntries(["alarm", "beep", "chime", "siren", "double", "triple", "gong", "signal", "urgent", "digital", "system-alert", "system-warning"].map((name) => [name, `assets/sounds/warnings/${name}.wav`]))
};
let preferences = { warningSound: true, warningVolume: 85, warningStyle: "alarm" };
let language = "de";
let currentUserName = "";
let currentUserIsAdmin = false;
let currentPage = 0;

const $ = (id) => document.getElementById(id);
const actionKeys = { shutdown: "shutdown", restart: "restart", logout: "logout", lock: "lock" };
const translations = window.powerControlLocales || {};

function t(key) { return translations[language]?.[key] ?? translations.de?.[key] ?? key; }
function actionLabel(action) { return t(actionKeys[action] || action); }

function applyLanguage(nextLanguage) {
  language = nextLanguage === "en" ? "en" : "de";
  document.documentElement.lang = language;
  localStorage.setItem("language", language);
  document.querySelectorAll("[data-i18n]").forEach((element) => { element.textContent = t(element.dataset.i18n); });
  document.querySelectorAll("[data-i18n-aria]").forEach((element) => { element.setAttribute("aria-label", t(element.dataset.i18nAria)); });
  document.querySelectorAll("[data-i18n-title]").forEach((element) => { element.title = t(element.dataset.i18nTitle); });
  document.querySelectorAll("[data-language]").forEach((button) => {
    const selected = button.dataset.language === language;
    button.classList.toggle("active", selected);
    button.setAttribute("aria-pressed", String(selected));
  });
  updateCurrentUserLabel();
  updateTimerDurationLabel();
}

function addSoundStyleControls() {
  const addSelect = (toggleId, selectId, labelKey, options) => {
    if ($(selectId)) return;
    const row = document.createElement("label");
    row.className = "settings-select sound-style";
    row.innerHTML = `<span data-i18n="${labelKey}"></span><div class="sound-style-control"><select id="${selectId}">${options.map(({ value, key }) => `<option value="${value}" data-i18n="${key}"></option>`).join("")}</select><button type="button" data-sound-preview="${selectId}" data-i18n="soundTest"></button></div>`;
    $(toggleId).closest("label").insertAdjacentElement("afterend", row);
  };
  addSelect("settings-warning-sound", "settings-warning-style", "timerSoundStyle", [
    { value: "alarm", key: "soundAlarm" }, { value: "beep", key: "soundBeep" }, { value: "chime", key: "soundChime" }, { value: "siren", key: "soundSiren" }, { value: "double", key: "soundDouble" },
    { value: "triple", key: "soundTriple" }, { value: "gong", key: "soundGong" }, { value: "signal", key: "soundSignal" }, { value: "urgent", key: "soundUrgent" }, { value: "digital", key: "soundDigital" }, { value: "system-alert", key: "soundSystemAlert" }, { value: "system-warning", key: "soundSystemWarning" }
  ]);
  if (!$("settings-theme").querySelector('option[value="dark"]')) {
    const option = document.createElement("option");
    option.value = "dark";
    option.dataset.i18n = "darkTheme";
    $("settings-theme").append(option);
  }
  const themeSelect = $("settings-theme");
  const pinkNeon = themeSelect.querySelector('option[value="neon"]');
  const classic = themeSelect.querySelector('option[value="classic"]');
  if (!themeSelect.querySelector('option[value="neon-blue"]')) {
    const blueNeon = document.createElement("option");
    blueNeon.value = "neon-blue";
    blueNeon.textContent = "Neon Blau";
    pinkNeon.insertAdjacentElement("beforebegin", blueNeon);
  }
  if (pinkNeon) {
    pinkNeon.removeAttribute("data-i18n");
    pinkNeon.textContent = "Neon Pink";
  }
  if (!themeSelect.querySelector('option[value="neon-green"]')) {
    const greenNeon = document.createElement("option");
    greenNeon.value = "neon-green";
    greenNeon.textContent = "Neon Grün";
    classic.insertAdjacentElement("beforebegin", greenNeon);
  }
  [
    ["retro", "Retro Terminal"],
    ["linux", "Linux Mint"],
    ["graphite", "Graphit"]
  ].forEach(([value, label]) => {
    if (!themeSelect.querySelector(`option[value="${value}"]`)) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = label;
      classic.insertAdjacentElement("beforebegin", option);
    }
  });
  if (classic) {
    classic.removeAttribute("data-i18n");
    classic.textContent = "Windows Classic";
  }
  if (!$("settings-autostart")) {
    const row = document.createElement("label");
    row.className = "settings-row settings-autostart";
    row.innerHTML = `<span><strong data-i18n="startWindows"></strong><small data-i18n="startWindowsHint"></small></span><input id="settings-autostart" type="checkbox">`;
    $("settings-window-size").closest("label").insertAdjacentElement("afterend", row);
  }
}

function updateCurrentUserLabel() {
  const label = $("current-user");
  if (label) label.textContent = currentUserName ? `${t("currentUser")}: ${currentUserName}` : t("currentUserUnknown");
  const role = $("current-user-role");
  if (role) {
    const labels = language === "en"
      ? (currentUserIsAdmin ? { title: "Administrator", description: "Full system access" } : { title: "Standard user", description: "Limited system access" })
      : (currentUserIsAdmin ? { title: "Administrator", description: "Volle Systemrechte" } : { title: "Standardbenutzer", description: "Eingeschränkte Systemrechte" });
    role.className = `user-role ${currentUserIsAdmin ? "admin" : "standard"}`;
    role.textContent = `${labels.title} · ${labels.description}`;
  }
}

async function loadCurrentUser() {
  const account = await api.getCurrentUser();
  currentUserName = account?.username || "";
  currentUserIsAdmin = account?.isAdmin === true;
  updateCurrentUserLabel();
}

function setupPages() {
  const dashboard = document.querySelector("main.grid");
  dashboard.querySelector(".autostart-row")?.remove();
  const viewport = document.createElement("div");
  const track = document.createElement("div");
  const usersPage = document.createElement("section");
  viewport.className = "page-viewport";
  track.className = "page-track";
  usersPage.className = "users-page";
  usersPage.innerHTML = `<div class="card users-card"><div class="users-heading"><div class="card-heading"><img class="heading-icon image-icon" src="assets/icons/log.svg" alt=""><div><h2 data-i18n="usersTitle"></h2><p data-i18n="usersSubtitle"></p><div class="current-user-info"><small id="current-user"></small><span id="current-user-role" class="user-role"></span></div></div></div><div class="users-actions"><button id="refresh-users" type="button"><span data-i18n="refresh"></span></button><button id="change-password" type="button"><span data-i18n="changePassword"></span></button></div></div><div id="users-list" class="users-list" aria-live="polite"></div></div><div class="users-bottom-grid"></div>`;
  dashboard.parentNode.insertBefore(viewport, dashboard);
  viewport.append(track);
  track.append(dashboard, usersPage);
  const usersBottom = usersPage.querySelector(".users-bottom-grid");
  const usersStatusCard = dashboard.querySelector(".status-card").cloneNode(true);
  const usersLogCard = dashboard.querySelector(".log-card").cloneNode(true);
  usersStatusCard.classList.add("users-status-card");
  usersLogCard.classList.add("users-log-card");
  usersStatusCard.querySelector("#status").id = "users-status";
  usersLogCard.querySelector("#log").id = "users-log";
  usersLogCard.querySelector("#refresh-log").id = "refresh-users-log";
  usersLogCard.querySelector("#clear-log").id = "clear-users-log";
  usersLogCard.querySelector("#open-log").id = "open-users-log";
  usersBottom.append(usersStatusCard, usersLogCard);
  const navigation = document.createElement("div");
  navigation.className = "page-navigation";
  navigation.innerHTML = '<button id="page-prev" type="button" aria-label="Previous page">‹</button><span id="page-indicator">1 / 2</span><button id="page-next" type="button" aria-label="Next page">›</button>';
  document.querySelector(".app-header").append(navigation);
  $("page-prev").addEventListener("click", () => showPage(0));
  $("page-next").addEventListener("click", () => showPage(1));
  $("refresh-users").addEventListener("click", loadLocalUsers);
  $("change-password").addEventListener("click", openSignInSettings);
  $("refresh-users-log").addEventListener("click", loadLogs);
  $("clear-users-log").addEventListener("click", clearLogs);
  $("open-users-log").addEventListener("click", () => api.openLog());
  let startX = 0;
  viewport.addEventListener("pointerdown", (event) => { startX = event.clientX; });
  viewport.addEventListener("pointerup", (event) => { if (Math.abs(event.clientX - startX) > 60) showPage(event.clientX < startX ? 1 : 0); });
  showPage(0);
}

async function openSignInSettings() {
  const opened = await api.openSignInSettings();
  setStatus(opened ? t("signInOpened") : t("signInFailed"), !opened);
}

async function clearLogs() {
  if (await confirmAction(t("clearLogConfirm"))) {
    await api.clearLogs();
    loadLogs();
  }
}

function showPage(page) {
  currentPage = page === 1 ? 1 : 0;
  document.querySelector(".page-track").style.transform = `translateX(-${currentPage * 50}%)`;
  $("page-prev").disabled = currentPage === 0;
  $("page-next").disabled = currentPage === 1;
  $("page-indicator").textContent = `${currentPage + 1} / 2`;
  if (currentPage === 1) loadLocalUsers();
}

async function loadLocalUsers() {
  const list = $("users-list");
  if (!list) return;
  list.textContent = t("usersLoading");
  const users = await api.getLocalUsers();
  if (!users.length) { list.textContent = t("usersEmpty"); return; }
  list.innerHTML = users.map((user) => `<div class="user-row"><span class="user-avatar">◉</span><span class="user-name">${escapeHtml(user.Name || "")}</span><span class="user-description">${escapeHtml(user.Description || "—")}</span><span class="user-state ${user.Enabled ? "enabled" : "disabled"}">${user.Enabled ? t("userEnabled") : t("userDisabled")}</span></div>`).join("");
}

function playTone(frequency, duration, volume, type = "sine") {
  const AudioContext = window.AudioContext || window.webkitAudioContext;
  if (!AudioContext) return;
  audioContext ??= new AudioContext();
  if (audioContext.state === "suspended") audioContext.resume().catch(() => {});
  const oscillator = audioContext.createOscillator();
  const gain = audioContext.createGain();
  oscillator.type = type;
  oscillator.frequency.setValueAtTime(frequency, audioContext.currentTime);
  gain.gain.setValueAtTime(volume, audioContext.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.001, audioContext.currentTime + duration);
  oscillator.connect(gain).connect(audioContext.destination);
  oscillator.start();
  oscillator.stop(audioContext.currentTime + duration);
}

function playPattern(notes, duration, volume, type, gap = 60) {
  notes.forEach((frequency, index) => window.setTimeout(() => playTone(frequency, duration, volume * (1 - index * 0.1), type), index * gap));
}

function playSoundAsset(category, style, volume, fallback) {
  const source = soundAssets[category]?.[style];
  if (!source) { fallback(); return; }
  const audio = new Audio(source);
  let fellBack = false;
  const useFallback = () => {
    if (fellBack) return;
    fellBack = true;
    fallback();
  };
  audio.volume = Math.max(0, Math.min(1, volume));
  audio.addEventListener("error", useFallback, { once: true });
  audio.play().catch(useFallback);
}

function playWarningSound() {
  if (!preferences.warningSound) return;
  playSoundAsset("warning", preferences.warningStyle, preferences.warningVolume / 100, playWarningToneFallback);
}

function playWarningToneFallback() {
  if (!preferences.warningSound) return;
  const volume = 0.38 * (preferences.warningVolume / 100);
  const styles = {
    alarm: [[820, 1120], .2, "square", 220], beep: [[980, 980], .18, "square", 210], chime: [[660, 880], .28, "sine", 160], siren: [[620, 920, 620, 920], .14, "sawtooth", 125], double: [[760, 760], .21, "square", 260],
    triple: [[860, 860, 860], .14, "square", 180], gong: [[330, 495], .42, "sine", 110], signal: [[1040, 1320], .17, "triangle", 165], urgent: [[1240, 840, 1240, 840], .12, "square", 95], digital: [[680, 1020, 1360], .16, "square", 130]
  };
  const [notes, duration, type, gap] = styles[preferences.warningStyle] || styles.alarm;
  playPattern(notes, duration, volume, type, gap);
}

function updateIconTheme(theme) {
  document.querySelectorAll('img[src*="assets/icons/"]').forEach((image) => {
    const iconName = image.dataset.iconName || image.getAttribute("src").split("/").pop().replace(".svg", "");
    image.dataset.iconName = iconName;
    image.src = theme !== "neon" && theme !== "neon-blue" && theme !== "neon-green" ? `assets/icons/classic/${iconName}.svg` : `assets/icons/${iconName}.svg`;
  });
}

function setStatus(message, isError = false) {
  const status = $("status");
  status.textContent = message;
  status.classList.toggle("error", isError);
  const usersStatus = $("users-status");
  if (usersStatus) {
    usersStatus.textContent = message;
    usersStatus.classList.toggle("error", isError);
  }
}

function escapeHtml(value) {
  return String(value).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

async function loadLogs() {
  const log = $("log");
  const data = await api.getLogs();
  const markup = data
    ? escapeHtml(data).split("\n").filter(Boolean).map((line) => `<div>●&nbsp;&nbsp; ${line}</div>`).join("")
    : `<span class="muted">●&nbsp;&nbsp; ${t("logEmpty")}</span>`;
  log.innerHTML = markup;
  log.scrollTop = log.scrollHeight;
  const usersLog = $("users-log");
  if (usersLog) {
    usersLog.innerHTML = markup;
    usersLog.scrollTop = usersLog.scrollHeight;
  }
}

function confirmAction(message) {
  return new Promise((resolve) => {
    const modal = $("modal");
    $("modal-text").textContent = message;
    modal.classList.remove("hidden");
    const finish = (confirmed) => { modal.classList.add("hidden"); $("modal-ok").onclick = null; $("modal-cancel").onclick = null; resolve(confirmed); };
    $("modal-ok").onclick = () => finish(true);
    $("modal-cancel").onclick = () => finish(false);
    $("modal-cancel").focus();
  });
}

async function runAction(action, delay = 0, confirm = true) {
  const actionName = actionLabel(action);
  if (confirm && !(await confirmAction(`${actionName}${delay ? ` in ${formatTime(delay)}` : ""}?`))) return false;
  const result = await api.runAction(action, delay);
  setStatus(result.ok ? `${actionName} ${delay ? t("planned") : t("running")}.` : result.message, !result.ok);
  if (result.ok) loadLogs();
  return result.ok;
}

function formatTime(seconds) {
  const minutes = Math.floor(seconds / 60);
  const remaining = seconds % 60;
  return `${minutes}:${String(remaining).padStart(2, "0")}`;
}

function updateTimerDurationLabel() {
  const input = $("timer-duration");
  const label = $("timer-duration-label");
  if (!input || !label) return;
  const seconds = Number(input.value);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.round((seconds % 3600) / 60);
  const hourText = hours === 1 ? `1 ${t("hour")}` : `${hours} ${t("hours")}`;
  const minuteText = minutes === 1 ? `1 ${t("minute")}` : `${minutes} ${t("minutes")}`;
  label.textContent = hours && minutes ? `${hourText} ${minuteText}` : hours ? hourText : minuteText;
}

function adjustTimerDuration(delta) {
  const input = $("timer-duration");
  if (!input) return;
  input.value = String(Math.min(86400, Math.max(60, Number(input.value) + delta)));
  updateTimerDurationLabel();
}

function setTimerActive(active) {
  $("timer-abort").disabled = !active;
  $("timer-shutdown").disabled = active;
  $("timer-restart").disabled = active;
  document.querySelectorAll("[data-action]").forEach((button) => { button.disabled = active; });
  api.setTimerActive(active, active ? activeTimer : 0).catch(() => {});
}

async function startTimer(action) {
  if (timerStarting || timerId !== null) return;
  timerStarting = true;
  try {
  const seconds = Number($("timer-duration").value);
  if (!Number.isInteger(seconds) || seconds < 60 || seconds > 86400) return;
  if (!(await confirmAction(`${actionLabel(action)} in ${formatTime(seconds)} ${t("planned")}?`))) return;
  const started = await runAction(action, seconds, false);
  if (!started) return;
  activeTimer = seconds;
  timerDeadline = Date.now() + seconds * 1000;
  setTimerActive(true);
  $("countdown").textContent = `⏳ ${t("remaining")} ${formatTime(activeTimer)}`;
  timerId = window.setInterval(() => {
    const previous = activeTimer;
    activeTimer = Math.max(0, Math.ceil((timerDeadline - Date.now()) / 1000));
    $("countdown").textContent = activeTimer > 0 ? `⏳ ${t("remaining")} ${formatTime(activeTimer)}` : t("executing");
    if (previous > 5 && activeTimer <= 5 && activeTimer > 0) playWarningSound();
    if (activeTimer <= 0) { window.clearInterval(timerId); timerId = null; setTimerActive(false); }
  }, 1000);
  } finally { timerStarting = false; }
}

async function abortTimer() {
  const result = await api.runAction("abort");
  if (!result.ok) { setStatus(result.message || t("noTimer"), true); return; }
  if (timerId) window.clearInterval(timerId);
  timerId = null;
  activeTimer = null;
  setTimerActive(false);
  $("countdown").textContent = "";
  setStatus(result.ok ? t("timerCanceled") : t("noTimer"), !result.ok);
  loadLogs();
}

function setTheme(theme) {
  const selectedTheme = ["standard", "neon-blue", "neon", "neon-green", "retro", "linux", "graphite", "classic", "dark"].includes(theme) ? theme : "standard";
  $("theme-style").href = selectedTheme === "neon-blue" ? "css/style.css" : selectedTheme === "neon" ? "css/neon.css" : selectedTheme === "neon-green" ? "css/neon-green.css" : selectedTheme === "retro" ? "css/retro.css" : selectedTheme === "linux" ? "css/linux.css" : selectedTheme === "graphite" ? "css/graphite.css" : selectedTheme === "classic" ? "css/classic.css" : selectedTheme === "dark" ? "css/dark.css" : "css/standard.css";
  updateIconTheme(selectedTheme);
  const themeButton = $("theme-toggle");
  if (themeButton) themeButton.innerHTML = selectedTheme === "neon-blue" ? "✦ <span>Neon Blau</span>" : selectedTheme === "neon" ? "🌙 <span>Neon Pink</span>" : selectedTheme === "neon-green" ? "✦ <span>Neon Grün</span>" : selectedTheme === "retro" ? "▣ <span>Retro Terminal</span>" : selectedTheme === "linux" ? "● <span>Linux Mint</span>" : selectedTheme === "graphite" ? "◆ <span>Graphit</span>" : selectedTheme === "classic" ? "◐ <span>Windows Classic</span>" : selectedTheme === "dark" ? "● <span>Dark</span>" : "☀ <span>Standard</span>";
  localStorage.setItem("theme", selectedTheme);
}

function openSettings() {
  $("settings-warning-sound").checked = preferences.warningSound;
  $("settings-warning-volume").value = preferences.warningVolume;
  $("settings-warning-style").value = preferences.warningStyle;
  updateSoundVolumeLabels();
  $("settings-theme").value = localStorage.getItem("theme") || "standard";
  $("settings-window-size").value = localStorage.getItem("windowSize") || "standard";
  $("settings-modal").classList.remove("hidden");
  $("settings-close").focus();
}

function closeSettings() { $("settings-modal").classList.add("hidden"); }

function savePreferences() {
  preferences = {
    warningSound: $("settings-warning-sound").checked,
    warningVolume: Number($("settings-warning-volume").value),
    warningStyle: $("settings-warning-style").value
  };
  localStorage.setItem("preferences", JSON.stringify(preferences));
}

function updateSoundVolumeLabels() {
  $("settings-warning-volume-value").textContent = `${$("settings-warning-volume").value}%`;
}

async function setWindowSize(sizeName) {
  const applied = await api.setWindowSize(sizeName);
  if (applied) {
    document.body.dataset.windowSize = sizeName;
    localStorage.setItem("windowSize", sizeName);
  }
}

document.addEventListener("DOMContentLoaded", async () => {
  try { preferences = { ...preferences, ...JSON.parse(localStorage.getItem("preferences") || "{}") }; } catch { /* gespeicherte Einstellung ignorieren */ }
  addSoundStyleControls();
  setupPages();
  $("timer-abort").querySelector("span").append($("countdown"));
  applyLanguage(localStorage.getItem("language") || "de");
  loadCurrentUser();
  document.querySelectorAll("[data-action]").forEach((button) => button.addEventListener("click", () => runAction(button.dataset.action)));
  $("timer-shutdown").addEventListener("click", () => startTimer("shutdown"));
  $("timer-restart").addEventListener("click", () => startTimer("restart"));
  document.querySelectorAll("[data-timer-adjust]").forEach((button) => button.addEventListener("click", () => adjustTimerDuration(Number(button.dataset.timerAdjust))));
  $("timer-abort").addEventListener("click", abortTimer);
  $("refresh-log").addEventListener("click", loadLogs);
  $("clear-log").addEventListener("click", clearLogs);
  $("open-log").addEventListener("click", () => api.openLog());
  $("settings-toggle").addEventListener("click", openSettings);
  $("settings-close").addEventListener("click", closeSettings);
  $("settings-warning-sound").addEventListener("change", savePreferences);
  $("settings-warning-style").addEventListener("change", savePreferences);
  document.querySelectorAll("[data-sound-preview]").forEach((button) => button.addEventListener("click", () => {
    savePreferences();
    playWarningSound();
  }));
  $("settings-warning-volume").addEventListener("input", () => { updateSoundVolumeLabels(); savePreferences(); });
  document.querySelectorAll("[data-language]").forEach((button) => button.addEventListener("click", () => applyLanguage(button.dataset.language)));
  $("settings-theme").addEventListener("change", (event) => setTheme(event.target.value));
  $("settings-window-size").addEventListener("change", (event) => setWindowSize(event.target.value));
  $("window-minimize").addEventListener("click", () => api.minimizeWindow());
  $("window-close").addEventListener("click", async () => { if (!(await api.closeWindow())) setStatus(t("timerCloseBlocked"), true); });
  $("settings-autostart").checked = await api.getAutostart();
  $("settings-autostart").addEventListener("change", async (event) => {
    const ok = await api.setAutostart(event.target.checked);
    if (!ok) event.target.checked = !event.target.checked;
    setStatus(ok ? (event.target.checked ? t("autoStartEnabled") : t("autoStartDisabled")) : t("autoStartFailed"), !ok);
  });
  setTheme(localStorage.getItem("theme") || "standard");
  setWindowSize(localStorage.getItem("windowSize") || "standard");
  updateTimerDurationLabel();
  loadLogs();
});
