const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("powerControl", {
  runAction: (action, delaySeconds) => ipcRenderer.invoke("power:action", action, delaySeconds),
  getLogs: () => ipcRenderer.invoke("log:read"),
  clearLogs: () => ipcRenderer.invoke("log:clear"),
  openLog: () => ipcRenderer.invoke("log:open"),
  getAutostart: () => ipcRenderer.invoke("autostart:get"),
  setAutostart: (enabled) => ipcRenderer.invoke("autostart:set", enabled),
  openSignInSettings: () => ipcRenderer.invoke("system:open-signin-settings"),
  getCurrentUser: () => ipcRenderer.invoke("system:current-user"),
  getLocalUsers: () => ipcRenderer.invoke("system:local-users"),
  minimizeWindow: () => ipcRenderer.send("window:minimize"),
  setWindowSize: (sizeName) => ipcRenderer.invoke("window:set-size", sizeName),
  closeWindow: () => ipcRenderer.invoke("window:close"),
  setTimerActive: (active, seconds) => ipcRenderer.invoke("timer:set-active", active, seconds)
});
