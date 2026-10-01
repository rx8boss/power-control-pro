const { app, BrowserWindow } = require("electron");
const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const input = path.join(root, "assets", "icons");
const output = path.join(root, "native", "assets");

app.whenReady().then(async () => {
  fs.mkdirSync(output, { recursive: true });
  for (const name of fs.readdirSync(input).filter((entry) => entry.endsWith(".svg"))) {
    const window = new BrowserWindow({ width: 128, height: 128, show: false, webPreferences: { sandbox: false } });
    const svg = fs.readFileSync(path.join(input, name)).toString("base64");
    const page = `<style>html,body{margin:0;width:128px;height:128px;background:transparent;overflow:hidden}img{display:block;width:128px;height:128px}</style><img src="data:image/svg+xml;base64,${svg}">`;
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(page)}`);
    const image = await window.webContents.capturePage({ x: 0, y: 0, width: 128, height: 128 });
    fs.writeFileSync(path.join(output, name.replace(".svg", ".png")), image.toPNG());
    window.destroy();
  }
  app.quit();
});
