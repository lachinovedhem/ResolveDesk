/**
 * Smoke test for the ResolveDesk MCP server: spawns it over stdio, initializes, and lists tools.
 * Verifies the protocol handshake and the tool surface — no ResolveDesk API needed.
 *
 *   node tools/mcp-smoke.mjs [path-to-resolvedesk-mcp.exe]
 */
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const exe = process.argv[2] ??
  path.join(here, "..", "src", "ResolveDesk.Mcp", "bin", "Debug", "net10.0", "resolvedesk-mcp.exe");

const EXPECTED = [
  "search_resolutions", "suggest_for_ticket", "ai_status",
  "get_ticket", "list_tickets", "ticket_history", "list_agents", "queue_stats", "add_comment",
  "triage_ticket",
];

const proc = spawn(exe, [], { stdio: ["pipe", "pipe", "pipe"] });
let stderr = "";
proc.stderr.on("data", (d) => { stderr += d; });

let buffer = "";
const pending = new Map();
let nextId = 0;

proc.stdout.on("data", (chunk) => {
  buffer += chunk;
  let i;
  while ((i = buffer.indexOf("\n")) >= 0) {
    const line = buffer.slice(0, i).trim();
    buffer = buffer.slice(i + 1);
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); } catch { continue; }
    if (msg.id !== undefined && pending.has(msg.id)) {
      pending.get(msg.id)(msg);
      pending.delete(msg.id);
    }
  }
});

const rpc = (method, params) => new Promise((resolve, reject) => {
  const id = ++nextId;
  pending.set(id, resolve);
  proc.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
  setTimeout(() => pending.has(id) && reject(new Error(`timeout on ${method}`)), 20000);
});

const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`);
};

try {
  const init = await rpc("initialize", {
    protocolVersion: "2024-11-05",
    capabilities: {},
    clientInfo: { name: "smoke", version: "0" },
  });
  check("initialize", !!init.result, init.result?.serverInfo?.name ?? JSON.stringify(init.error ?? {}));
  proc.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} }) + "\n");

  const list = await rpc("tools/list", {});
  const names = (list.result?.tools ?? []).map((t) => t.name).sort();
  check(`tools/list returns ${EXPECTED.length}`, names.length === EXPECTED.length, names.join(", "));

  const missing = EXPECTED.filter((e) => !names.includes(e));
  check("every expected tool present", missing.length === 0, missing.length ? `missing: ${missing}` : "");

  const described = (list.result?.tools ?? []).every((t) => (t.description ?? "").length > 40);
  check("every tool has a usable description", described);

  const search = (list.result?.tools ?? []).find((t) => t.name === "search_resolutions");
  const props = Object.keys(search?.inputSchema?.properties ?? {});
  // Injected services must not leak into the public schema.
  check("search_resolutions schema is clean", !props.includes("client"), props.join(", "));
} catch (err) {
  check("protocol", false, String(err));
}

const passed = results.filter(Boolean).length;
console.log(`\n${passed}/${results.length} passed`);
if (passed !== results.length && stderr) console.log(`\n--- server stderr ---\n${stderr.slice(0, 2000)}`);
proc.kill();
process.exit(passed === results.length ? 0 : 1);
