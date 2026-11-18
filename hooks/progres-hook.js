// Claude Code hook: zapíše stav session do ~/.claude/progres-lista/<session_id>.json.
// Použití: node progres-hook.js <running|attention|done|end>
// Nikdy nic nevypisuje (výstup UserPromptSubmit by šel do kontextu) a vždy končí kódem 0.
const fs = require("fs");
const os = require("os");
const path = require("path");

const dir = path.join(os.homedir(), ".claude", "progres-lista");
let state = process.argv[2] || "done";

let raw = "";
process.stdin.on("data", (c) => (raw += c));
process.stdin.on("end", () => {
  try {
    const input = JSON.parse(raw || "{}");
    const id = String(input.session_id || "unknown").replace(/[^\w-]/g, "");
    const file = path.join(dir, id + ".json");
    fs.mkdirSync(dir, { recursive: true });

    if (state === "end") {
      fs.rmSync(file, { force: true });
      return;
    }

    let prev = {};
    try { prev = JSON.parse(fs.readFileSync(file, "utf8")); } catch {}

    // Upozornění „čekám na tvůj vstup“ po dokončení není potřeba pozornosti, session je hotová.
    const message = input.message || "";
    if (state === "attention" &&
        (input.notification_type === "idle_prompt" || /waiting for your input/i.test(message))) {
      if (prev.state === "done") return;
      state = "done";
    }

    const cwd = input.cwd || prev.cwd || "";
    const data = {
      session_id: id,
      name: path.basename(cwd) || id.slice(0, 8),
      cwd,
      state,
      message: state === "attention" ? message : "",
      updated: Date.now(),
    };
    const tmp = file + ".tmp";
    fs.writeFileSync(tmp, JSON.stringify(data));
    fs.renameSync(tmp, file);
  } catch {}
});
