const { app, BrowserWindow, ipcMain, globalShortcut, Tray, Menu, nativeImage, shell } = require("electron");
const AutoLaunch = require("auto-launch");
const { execFile } = require("child_process");
const fs = require("fs");
const path = require("path");
const os = require("os");

let mainWindow;
let tray;
let logPath;
let timerActive = false;
let timerGuardTimeout;

const windowSizes = {
  compact: { width: 860, height: 620 },
  standard: { width: 900, height: 650 },
  large: { width: 1050, height: 760 },
  xlarge: { width: 1200, height: 870 }
};

const autoLauncher = new AutoLaunch({ name: "Power Control PRO" });

function fallbackIcon() {
  const svg = fs.readFileSync(path.join(__dirname, "assets", "app-icon.svg"), "utf8");
  return nativeImage.createFromDataURL(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
}

function getIcon() {
  const candidates = [
    path.join(__dirname, "build", "icon.ico"),
    path.join(__dirname, "assets", "app-icon.ico"),
    path.join(__dirname, "assets", "app-icon.png"),
    path.join(__dirname, "assets", "icon.ico")
  ];
  const iconFile = candidates.find(fs.existsSync);
  return iconFile ? nativeImage.createFromPath(iconFile) : fallbackIcon();
}

function appendLog(message) {
  if (!logPath) return;
  fs.appendFileSync(logPath, `[${new Date().toLocaleString()}] ${message}\n`, "utf8");
}

function runWindowsCommand(file, args = []) {
  return new Promise((resolve) => {
    execFile(file, args, { windowsHide: true }, (error) => {
      if (error) {
        console.error(`Command failed (${file}):`, error.message);
        resolve({ ok: false, message: "Fehler beim Ausführen der Aktion." });
        return;
      }
      resolve({ ok: true });
    });
  });
}

function listLocalUsers() {
  const command = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); Get-LocalUser | Select-Object Name,Enabled,Description,LastLogon | ConvertTo-Json -Compress";
  return new Promise((resolve) => {
    execFile("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], { windowsHide: true }, (error, stdout) => {
      if (error || !stdout.trim()) return resolve([]);
      try {
        const users = JSON.parse(stdout);
        resolve(Array.isArray(users) ? users : [users]);
      } catch {
        resolve([]);
      }
    });
  });
}

function isCurrentUserAdmin() {
  const command = "$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent()); [Console]::Write($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))";
  return new Promise((resolve) => {
    execFile("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], { windowsHide: true }, (error, stdout) => {
      resolve(!error && stdout.trim().toLowerCase() === "true");
    });
  });
}

function commandFor(action, delaySeconds = 0) {
  const delay = String(Math.max(0, Number.parseInt(delaySeconds, 10) || 0));
  switch (action) {
    case "shutdown": return { file: "shutdown.exe", args: ["/s", "/t", delay] };
    case "restart": return { file: "shutdown.exe", args: ["/r", "/t", delay] };
    case "logout": return { file: "shutdown.exe", args: ["/l"] };
    case "lock": return { file: "rundll32.exe", args: ["user32.dll,LockWorkStation"] };
    case "abort": return { file: "shutdown.exe", args: ["/a"] };
    default: return null;
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 900,
    height: 650,
    minWidth: 900,
    minHeight: 650,
    maxWidth: 900,
    maxHeight: 650,
    frame: false,
    resizable: false,
    maximizable: false,
    skipTaskbar: false,
    icon: getIcon(),
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true
    }
  });

  mainWindow.loadFile(path.join(__dirname, "index.html"));
  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.on("close", (event) => {
    if (timerActive && !app.isQuiting) {
      event.preventDefault();
      mainWindow.show();
      mainWindow.focus();
      return;
    }
    if (tray && !app.isQuiting) {
      event.preventDefault();
      mainWindow.hide();
    }
  });
}

function updateTrayMenu() {
  if (!tray) return;
  const actionsEnabled = !timerActive;
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: "⚡  Power Control PRO", enabled: false },
    { label: "Fenster öffnen", click: () => { mainWindow.show(); mainWindow.focus(); } },
    { type: "separator" },
    { label: timerActive ? "Timer aktiv – Schnellaktionen gesperrt" : "Schnellaktionen", enabled: false },
    { label: "Herunterfahren", enabled: actionsEnabled, click: () => runTrayAction("shutdown") },
    { label: "Neustart", enabled: actionsEnabled, click: () => runTrayAction("restart") },
    { label: "Windows sperren", enabled: actionsEnabled, click: () => runTrayAction("lock") },
    { type: "separator" },
    { label: "Programm beenden", enabled: actionsEnabled, click: () => { app.isQuiting = true; app.quit(); } }
  ]));
}

function createTray() {
  tray = new Tray(getIcon());
  tray.setToolTip("Power Control PRO");
  updateTrayMenu();
  tray.on("click", () => { mainWindow.show(); mainWindow.focus(); });
}

async function runTrayAction(action) {
  if (timerActive) {
    mainWindow.show();
    mainWindow.focus();
    return;
  }
  const command = commandFor(action);
  if (!command) return;
  const result = await runWindowsCommand(command.file, command.args);
  if (result.ok) appendLog(`${action} über Taskleisten-Menü`);
  else {
    mainWindow.show();
    mainWindow.focus();
  }
}

function registerIpc() {
  ipcMain.handle("power:action", async (_event, action, delaySeconds) => {
    const command = commandFor(action, delaySeconds);
    if (!command) return { ok: false, message: "Ungültige Aktion." };
    const result = await runWindowsCommand(command.file, command.args);
    if (result.ok) appendLog(`${action}${delaySeconds ? ` in ${delaySeconds} Sekunden` : ""}`);
    return result;
  });
  ipcMain.handle("log:read", () => logPath && fs.existsSync(logPath) ? fs.readFileSync(logPath, "utf8") : "");
  ipcMain.handle("log:clear", () => { if (logPath) fs.writeFileSync(logPath, "", "utf8"); return true; });
  ipcMain.handle("log:open", () => logPath ? shell.openPath(logPath) : "");
  ipcMain.handle("autostart:get", async () => { try { return await autoLauncher.isEnabled(); } catch { return false; } });
  ipcMain.handle("autostart:set", async (_event, enabled) => {
    try { if (enabled) await autoLauncher.enable(); else await autoLauncher.disable(); return true; } catch { return false; }
  });
  ipcMain.handle("system:open-signin-settings", async () => {
    const windowsRoot = process.env.SystemRoot || process.env.WINDIR || "C:\\Windows";
    const userAccounts = path.join(windowsRoot, "System32", "netplwiz.exe");
    try {
      if (fs.existsSync(userAccounts)) {
        const openError = await shell.openPath(userAccounts);
        if (!openError) return true;
      }
      await shell.openExternal("ms-settings:signinoptions");
      return true;
    } catch {
      return false;
    }
  });
  ipcMain.handle("system:current-user", async () => {
    try { return { username: os.userInfo().username, isAdmin: await isCurrentUserAdmin() }; } catch { return { username: "", isAdmin: false }; }
  });
  ipcMain.handle("system:local-users", () => listLocalUsers());
  ipcMain.handle("timer:set-active", (_event, active, seconds = 0) => {
    timerActive = Boolean(active);
    clearTimeout(timerGuardTimeout);
    if (timerActive) {
      const guardDelay = Math.max(1000, Number(seconds) * 1000 + 2000);
      timerGuardTimeout = setTimeout(() => { timerActive = false; updateTrayMenu(); }, guardDelay);
    }
    updateTrayMenu();
    return timerActive;
  });
  ipcMain.on("window:minimize", () => mainWindow?.minimize());
  ipcMain.handle("window:set-size", (_event, sizeName) => {
    const size = windowSizes[sizeName];
    if (!mainWindow || !size) return false;
    mainWindow.setMaximumSize(0, 0);
    mainWindow.setMinimumSize(0, 0);
    mainWindow.setSize(size.width, size.height);
    mainWindow.setMinimumSize(size.width, size.height);
    mainWindow.setMaximumSize(size.width, size.height);
    mainWindow.setResizable(false);
    return true;
  });
  ipcMain.handle("window:close", () => {
    if (timerActive) return false;
    app.isQuiting = true;
    app.quit();
    return true;
  });
}

app.whenReady().then(() => {
  app.setAppUserModelId("com.powercontrolpro.desktop");
  logPath = path.join(app.getPath("documents"), "power-control-pro.log");
  registerIpc();
  createWindow();
  createTray();
  globalShortcut.register("Control+Alt+L", () => runWindowsCommand("rundll32.exe", ["user32.dll,LockWorkStation"]));
  app.on("activate", () => mainWindow.show());
});

app.on("will-quit", () => globalShortcut.unregisterAll());
app.on("window-all-closed", (event) => event.preventDefault());
