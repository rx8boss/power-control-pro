const { contextBridge, ipcRenderer } = require("electron");
contextBridge.exposeInMainWorld("api", {
  run: (cmd) => ipcRenderer.invoke("run-cmd", cmd),
  saveLog: (msg) => ipcRenderer.invoke("save-log", msg),
  getLogs: () => ipcRenderer.invoke("get-logs"),
  clearLogs: () => ipcRenderer.invoke("clear-logs"),
  openLogFile: () => ipcRenderer.invoke("open-log-file"),
  toggleAutostart: (enabled) => ipcRenderer.invoke("toggle-autostart", enabled),
  getAutostart: () => ipcRenderer.invoke("get-autostart")
});
