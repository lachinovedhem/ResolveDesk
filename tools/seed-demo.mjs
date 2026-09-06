/**
 * Seeds a running ResolveDesk with a realistic ISP/telecom call-center archive, then demonstrates the
 * product's headline feature against it.
 *
 * Everything goes through the public API, so this exercises the same code path a real operator does —
 * no direct database access, no fixtures smuggled in behind the application.
 *
 *   node tools/seed-demo.mjs                 # seed, then run the demo
 *   node tools/seed-demo.mjs --demo-only     # just the demo against existing data
 *   RESOLVEDESK_API_URL=http://localhost:8080 RESOLVEDESK_API_TOKEN=... node tools/seed-demo.mjs
 *
 * The dataset lives in demo-corpus.mjs, shared with tools/validate-retrieval.mjs so the seeded data
 * and the tuning harness can never drift apart. Its open tickets are deliberately worded differently
 * from the resolved ones they correspond to — that is what makes this a demonstration.
 */

import { AGENTS, OPEN, RESOLVED } from "./demo-corpus.mjs";

const BASE = process.env.RESOLVEDESK_API_URL ?? "http://localhost:8080";
const TOKEN = process.env.RESOLVEDESK_API_TOKEN ?? "";
const DEMO_ONLY = process.argv.includes("--demo-only");

/* ────────────────────────────────────────────────────────────────────────────────────────────── */

const headers = {
  "Content-Type": "application/json",
  ...(TOKEN ? { Authorization: `Bearer ${TOKEN}` } : {}),
};

async function call(method, path, body) {
  const response = await fetch(`${BASE}/api/v1${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`${method} ${path} → ${response.status} ${(await response.text()).slice(0, 300)}`);
  }
  return response.status === 204 ? null : response.json();
}

const bar = (label) => console.log(`\n\x1b[1m${label}\x1b[0m\n${"─".repeat(label.length)}`);

async function preflight() {
  const health = await fetch(`${BASE}/health/live`).catch(() => null);
  if (!health?.ok) {
    console.error(
      `\nCannot reach the API at ${BASE}.\n` +
      `Start it first:  dotnet run --project src/ResolveDesk.WebApi --no-launch-profile\n`);
    process.exit(1);
  }
  const ai = await call("GET", "/ai/status");
  bar("AI configuration");
  console.log(`  chat       ${ai.chatProvider}/${ai.chatModel || "—"}  ${ai.chatReachable ? "✓ reachable" : "✗ not reachable"}`);
  console.log(`  embedding  ${ai.embeddingProvider}/${ai.embeddingModel || "—"} (${ai.embeddingDimensions}d)  ${ai.embeddingReachable ? "✓ reachable" : "✗ not reachable"}`);
  console.log(`  vectors    ${ai.vectorSearchAvailable ? "✓ pgvector available" : "✗ pgvector unavailable — keyword search only"}`);
  console.log(`  ${ai.detail ?? ""}`);
  return ai;
}

async function seed() {
  bar("Seeding the team");
  const users = [];
  for (const agent of AGENTS) {
    try {
      const created = await call("POST", "/users", agent);
      users.push({ ...agent, id: created.id });
      console.log(`  + ${agent.fullName} (${agent.role})`);
    } catch {
      console.log(`  · ${agent.fullName} already exists`);
    }
  }

  const existing = await call("GET", "/users");
  const coordinator = existing.find((u) => u.role === "Coordinator") ?? existing[0];
  const agents = existing.filter((u) => u.role === "Agent");

  bar("Seeding the resolved archive");
  for (const [index, ticket] of RESOLVED.entries()) {
    // `key` is the harness's identifier for the ticket, not part of the API contract.
    const { resolution, key, ...create } = ticket;
    const created = await call("POST", "/tickets", create);
    // Route it, then resolve it — the same path a coordinator and an agent walk.
    if (agents.length) {
      await call("POST", `/tickets/${created.id}/assign`, {
        assigneeId: agents[index % agents.length].id,
        coordinatorId: coordinator.id,
      });
    }
    // skipTriage: this is a historical archive being imported, not ten tickets being closed now.
    // Without it the queue fills with a resolution review per record and the demo waits on all of them.
    await call("POST", `/tickets/${created.id}/status`,
      { status: "Resolved", resolution, skipTriage: true });
    console.log(`  ✓ ${created.reference}  ${created.title}`);
  }

  bar("Seeding the open queue");
  const open = [];
  for (const ticket of OPEN) {
    const { expect, expectKey, ...create } = ticket;
    const created = await call("POST", "/tickets", create);
    open.push({ ...created, expect });
    console.log(`  ○ ${created.reference}  ${created.title}`);
  }
  return open;
}

async function demo(open) {
  bar("What the archive says about each new ticket");

  for (const ticket of open) {
    console.log(`\n\x1b[1m${ticket.reference} — ${ticket.title}\x1b[0m`);
    console.log(`  \x1b[2mexpected match: ${ticket.expect}\x1b[0m`);

    const result = await call("GET", `/tickets/${ticket.id}/suggestions?limit=3`);
    console.log(`  strategy: ${result.strategy} · ${result.matches.length} of ${result.considered} candidates · ${result.elapsedMs}ms`);

    if (result.matches.length === 0) {
      console.log("  (nothing matched)");
    } else {
      for (const match of result.matches) {
        // A vector match reports cosine similarity, which reads sensibly as a percentage. A keyword
        // match reports ts_rank_cd relevance, where 0.04 can be a decisive first place — showing
        // that as "4%" would misrepresent it, so each kind is printed in its own units.
        const score = match.matchKind === "keyword"
          ? `rel ${match.similarity.toFixed(3)}`
          : `${(match.similarity * 100).toFixed(0)}% sim`;
        console.log(`   ${score.padStart(9)}  [${match.matchKind.padEnd(8)}] ${match.sourceReference}  ${match.title}`);
      }
      if (result.answer) {
        console.log(`\n  \x1b[1mDrafted answer\x1b[0m (${result.model}):`);
        for (const line of result.answer.split("\n")) console.log(`   │ ${line}`);
      }
    }

    // Assessment runs in the background right after creation, so it may not be ready on the first look.
    const assessment = await waitForAssessment(ticket.id);
    if (assessment) {
      console.log(`\n  \x1b[1mAssessment\x1b[0m (${assessment.model}):`);
      console.log(`   difficulty ${assessment.difficulty}/5 · ~${assessment.estimatedMinutes} min` +
                  ` · ${assessment.suggestedCategory ?? "—"} · ${assessment.suggestedPriority ?? "—"}` +
                  ` · confidence ${(assessment.confidence * 100).toFixed(0)}%`);
      console.log(`   "${assessment.summary}"`);
      if (assessment.duplicateReference) {
        console.log(`   \x1b[33mpossible duplicate of ${assessment.duplicateReference}\x1b[0m`);
      }
    } else {
      console.log("\n  (no assessment within the timeout — the local model may still be working)");
    }

    const routing = await call("GET", `/tickets/${ticket.id}/routing`);
    console.log(`\n  \x1b[1mRouting\x1b[0m: ${routing.assigneeName ?? "no recommendation"}`);
    console.log(`   ${routing.reason}`);
    for (const candidate of routing.candidates.slice(0, 3)) {
      console.log(`   ${String(Math.round(candidate.score * 100)).padStart(3)}  ${candidate.fullName.padEnd(22)}` +
                  ` solved ${candidate.solvedSimilar}, skills ${candidate.skillMatches}, open ${candidate.openTickets}`);
    }
  }
}

/**
 * The triage worker is asynchronous by design. Assessing one ticket runs two model calls — a
 * suggestion lookup and the assessment itself — which on a CPU-only local model is tens of seconds,
 * not the handful the first version of this script allowed. It waits up to two minutes and says so,
 * rather than reporting a slow model as a missing one.
 */
async function waitForAssessment(ticketId, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs;
  let announced = false;
  while (Date.now() < deadline) {
    const assessment = await call("GET", `/tickets/${ticketId}/assessment`);
    if (assessment) return assessment;
    if (!announced) {
      process.stdout.write("  waiting for the triage worker");
      announced = true;
    }
    process.stdout.write(".");
    await new Promise((resolve) => setTimeout(resolve, 3000));
  }
  if (announced) process.stdout.write("\n");
  return null;
}

/** Shows the resolution review: the tidied reply and how it compares with past practice. */
async function demoResolutionReview() {
  bar("Resolving a ticket: tidy-up and comparison with past practice");

  const created = await call("POST", "/tickets", {
    title: "Line drops each evening on a VDSL connection",
    description: "Customer loses sync every evening around eight for about twenty minutes.",
    category: "Connectivity", priority: "High", source: "Phone", customerName: "Demo Müştəri",
  });
  console.log(`  created ${created.reference}`);

  const resolution =
    "checked line stats, snr margin was down to 4db at peak. dropped profile 17a -> 8b and turned on " +
    "sra. margin holding 9-11db now, no retrains overnight. told customer to call back if it repeats, " +
    "next step would be the drop cable.";
  console.log(`  agent wrote:\n   │ ${resolution}`);

  await call("POST", `/tickets/${created.id}/status`, { status: "Resolved", resolution });

  for (let attempt = 0; attempt < 12; attempt++) {
    const review = await call("GET", `/tickets/${created.id}/resolution-review`);
    if (review) {
      console.log(`\n  \x1b[1mVerdict\x1b[0m: ${review.verdict} — ${review.verdictDetail}`);
      console.log(`  compared against: ${review.comparedReferences || "—"}`);
      console.log(`\n  \x1b[1mCustomer-ready reply\x1b[0m:`);
      for (const line of review.polishedReply.split("\n")) console.log(`   │ ${line}`);
      console.log(`\n  \x1b[1mInternal summary\x1b[0m:`);
      for (const line of review.internalNote.split("\n")) console.log(`   │ ${line}`);
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 2500));
  }
  console.log("  (no review produced — is a chat model configured?)");
}

const ai = await preflight();
// In --demo-only the tickets come back from the API without the corpus's expectations attached,
// so they are matched back by title. Without that the run still works but prints "—" for every
// expected match, which is exactly the column that makes the output checkable.
const open = DEMO_ONLY
  ? (await call("GET", "/tickets?status=Open&limit=20")).items.map((t) => ({
      ...t,
      expect: OPEN.find((o) => o.title === t.title)?.expect ?? "—",
    }))
  : await seed();

await demo(open);
if (!DEMO_ONLY) await demoResolutionReview();

bar("Summary");
const stats = await call("GET", "/stats");
console.log(`  ${JSON.stringify(stats)}`);
if (!ai.vectorSearchAvailable) {
  console.log("\n  Semantic search is off, so the matches above are keyword-only.");
  console.log("  Install pgvector and pull an embedding model to see the paraphrased tickets match too.");
}
