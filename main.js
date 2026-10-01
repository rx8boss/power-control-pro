const { app, BrowserWindow, ipcMain, globalShortcut, Tray, Menu, nativeImage, shell } = require("electron");
const AutoLaunch = require("auto-launch");
const path = require("path");
const { exec } = require("child_process");
const fs = require("fs");

const isDev = !app.isPackaged;
let mainWin;
let tray;
let logPath;

const autoLauncher = new AutoLaunch({ name: "Power Control PRO" });

function iconPath() {
  const candidates = [
    path.join(__dirname, "build", "icon.ico"),
    path.join(__dirname, "assets", "icon.png"),
    path.join(__dirname, "assets", "icon.ico")
  ];
  return candidates.find((p) => fs.existsSync(p));
}

function loadIcon() {
  const file = iconPath();
  if (file) return nativeImage.createFromPath(file);
  return nativeImage.createFromDataURL("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAPElEQVR4nO3OMQEAMAgDsGn/nLEDy0gC+c4kGAwGg8FgMBgMBoPBYDAYDAaDwWAwGAwGg8FgMBgM/gcJDwAB3tUNhQAAAABJRU5ErkJggg==");
}

function createMain() {
  const icon = loadIcon();
  mainWin = new BrowserWindow({
    width: 950,
    height: 950,
    icon,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });
  mainWin.loadFile(path.join(__dirname, "index.html"));
  if (isDev) mainWin.webContents.openDevTools();
  mainWin.webContents.on("devtools-opened", () => { if (!isDev) mainWin.webContents.closeDevTools(); });
}

ipcMain.handle("run-cmd", async (_, cmd) => new Promise((resolve) => {
  exec(cmd, { windowsHide: true }, (err) => resolve(err ? "Fehler" : "OK"));
}));
ipcMain.handle("save-log", (_, msg) => {
  if (logPath) fs.appendFileSync(logPath, "[" + new Date().toLocaleString() + "] " + msg + "\n");
});
ipcMain.handle("get-logs", () => !logPath || !fs.existsSync(logPath) ? "" : fs.readFileSync(logPath, "utf-8"));
ipcMain.handle("clear-logs", () => { if (logPath) fs.writeFileSync(logPath, ""); return "CLEARED"; });
ipcMain.handle("open-log-file", () => { if (logPath) shell.openPath(logPath); });
ipcMain.handle("toggle-autostart", async (_, enabled) => enabled ? autoLauncher.enable() : autoLauncher.disable());
ipcMain.handle("get-autostart", async () => { try { return await autoLauncher.isEnabled(); } catch { return false; } });

function registerHotkeys() {
  globalShortcut.register("Control+Shift+I", () => {});
  globalShortcut.register("F12", () => {});
  globalShortcut.register("Control+Alt+S", () => exec("shutdown -s -t 0"));
  globalShortcut.register("Control+Alt+R", () => exec("shutdown -r -t 0"));
  globalShortcut.register("Control+Alt+L", () => exec("rundll32.exe user32.dll,LockWorkStation"));
}

function createTray() {
  try {
    tray = new Tray(loadIcon());
    const menu = Menu.buildFromTemplate([
      { label: "Öffnen", click: () => mainWin.show() },
      { label: "Herunterfahren", click: () => exec("shutdown -s -t 0") },
      { label: "Neustart", click: () => exec("shutdown -r -t 0") },
      { label: "Sperren", click: () => exec("rundll32.exe user32.dll,LockWorkStation") },
      { type: "separator" },
      { label: "Beenden", click: () => app.quit() }
    ]);
    tray.setToolTip("Power Control PRO");
    tray.setContextMenu(menu);
    tray.on("click", () => { if (mainWin) { mainWin.show(); mainWin.focus(); } });
  } catch (err) { console.error("Tray konnte nicht erstellt werden:", err); }
}

app.whenReady().then(() => {
  logPath = path.join(app.getPath("documents"), "power-control-log.txt");
  createMain();
  createTray();
  registerHotkeys();
  try { require("./updater").initUpdater(mainWin); } catch (err) { console.log("Updater nicht aktiv:", err.message); }
});
app.on("will-quit", () => globalShortcut.unregisterAll());
