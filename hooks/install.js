// Přidá hooky Progres lišty do ~/.claude/settings.json (idempotentně, se zálohou).
// Použití: node hooks/install.js        (odinstalace: node hooks/install.js --remove)
const fs = require("fs");
const os = require("os");
const path = require("path");

const settingsPath = path.join(os.homedir(), ".claude", "settings.json");
const script = path.join(__dirname, "progres-hook.js").replace(/\\/g, "/");
const MARK = "progres-hook.js";

const events = {
  SessionStart: "done",
  UserPromptSubmit: "running",
  PostToolUse: "running",
  Notification: "attention",
  Stop: "done",
  SessionEnd: "end",
};

const settings = fs.existsSync(settingsPath) ? JSON.parse(fs.readFileSync(settingsPath, "utf8")) : {};
fs.copyFileSync(settingsPath, settingsPath + ".bak-progres-lista");
settings.hooks = settings.hooks || {};

// Nejdřív odstraní staré záznamy Progres lišty.
for (const ev of Object.keys(settings.hooks)) {
  settings.hooks[ev] = settings.hooks[ev]
    .map((g) => ({ ...g, hooks: (g.hooks || []).filter((h) => !String(h.command).includes(MARK)) }))
    .filter((g) => g.hooks.length > 0);
  if (settings.hooks[ev].length === 0) delete settings.hooks[ev];
}

if (!process.argv.includes("--remove")) {
  for (const [ev, state] of Object.entries(events)) {
    const group = { hooks: [{ type: "command", command: `node "${script}" ${state}`, timeout: 10 }] };
    if (ev === "PostToolUse") group.matcher = "*";
    (settings.hooks[ev] = settings.hooks[ev] || []).push(group);
  }
}

if (Object.keys(settings.hooks).length === 0) delete settings.hooks;
fs.writeFileSync(settingsPath, JSON.stringify(settings, null, 2) + "\n");
console.log(process.argv.includes("--remove") ? "Hooky odebrány." : "Hooky nainstalovány do " + settingsPath);
