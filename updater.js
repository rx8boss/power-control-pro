function initUpdater() {
  try {
    const { autoUpdater } = require("electron-updater");
    autoUpdater.checkForUpdatesAndNotify();
  } catch {
    console.log("electron-updater nicht installiert – Update-Check übersprungen");
  }
}
module.exports = { initUpdater };
