/**
 * Validates the retrieval layer's tuning against the real embedding model — with no database.
 *
 * Two constants decide whether the headline feature works at all:
 *   MinSimilarity (0.35) — below this a match is discarded as noise
 *   RRF k        (60)    — how vector and full-text rankings are fused
 *
 * Both were chosen from the literature, not measured. This harness measures them: it embeds the demo
 * archive and the deliberately-reworded open tickets with the configured model, and checks that each
 * open ticket's true counterpart ranks first and clears the threshold. If the margin is thin, the
 * demo would show an empty panel and the number is wrong — better to find that here.
 *
 *   node tools/validate-retrieval.mjs
 */
import { OPEN, RESOLVED, queryContent, resolvedContent } from "./demo-corpus.mjs";

const OLLAMA = process.env.OLLAMA_URL ?? "http://localhost:11434";
const MODEL = process.env.EMBED_MODEL ?? "nomic-embed-text";

// Must match SuggestionService.MinSimilarity and RrfK.
const MIN_SIMILARITY = Number(process.env.MIN_SIMILARITY ?? 0.50);
const RRF_K = 60;

async function embed(texts) {
  const response = await fetch(`${OLLAMA}/api/embed`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ model: MODEL, input: texts }),
  });
  if (!response.ok) {
    throw new Error(`${MODEL} embed failed: ${response.status} ${await response.text()}`);
  }
  const { embeddings } = await response.json();
  return embeddings;
}

const cosine = (a, b) => {
  let dot = 0, na = 0, nb = 0;
  for (let i = 0; i < a.length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
  return dot / (Math.sqrt(na) * Math.sqrt(nb));
};

/**
 * The lexical half, approximated: PostgreSQL's plainto_tsquery + ts_rank_cd reduce to term overlap
 * on the same normalised tokens. Close enough to tell whether fusion changes the winner.
 */
const STOP = new Set(["the", "a", "an", "and", "or", "but", "is", "are", "was", "were", "on", "in",
  "at", "to", "of", "for", "it", "its", "that", "this", "with", "not", "no", "there", "then", "they",
  "have", "has", "had", "be", "been", "by", "from", "as", "all", "any", "our", "we", "i", "my"]);

const tokens = (text) => new Set(
  text.toLowerCase().split(/[^a-z0-9]+/).filter((w) => w.length > 2 && !STOP.has(w)));

function lexicalScore(query, document) {
  const q = tokens(query), d = tokens(document);
  let overlap = 0;
  for (const term of q) if (d.has(term)) overlap++;
  return q.size === 0 ? 0 : overlap / q.size;
}

/** Reciprocal rank fusion, identical in shape to SuggestionService.Fuse. */
function fuse(semanticRanking, lexicalRanking) {
  const scores = new Map();
  for (const list of [semanticRanking, lexicalRanking]) {
    list.forEach((key, rank) => {
      scores.set(key, (scores.get(key) ?? 0) + 1 / (RRF_K + rank + 1));
    });
  }
  return [...scores.entries()].sort((a, b) => b[1] - a[1]).map(([key]) => key);
}

const bar = (label) => console.log(`\n\x1b[1m${label}\x1b[0m\n${"─".repeat(label.length)}`);

console.log(`model: ${MODEL}  ·  min similarity: ${MIN_SIMILARITY}  ·  RRF k: ${RRF_K}`);

const archiveVectors = await embed(RESOLVED.map(resolvedContent));
const queryVectors = await embed(OPEN.map(queryContent));

if (archiveVectors[0].length !== 768) {
  console.log(`\n\x1b[33mWARNING\x1b[0m ${MODEL} emits ${archiveVectors[0].length}-d vectors, ` +
    `but Ai:Embedding:Dimensions is 768. Fix the setting before seeding.`);
}

const results = [];

bar("Per-ticket retrieval");

for (const [index, open] of OPEN.entries()) {
  const query = queryVectors[index];

  const scored = RESOLVED.map((resolved, position) => ({
    key: resolved.key,
    title: resolved.title,
    similarity: cosine(query, archiveVectors[position]),
    lexical: lexicalScore(queryContent(open), resolvedContent(resolved)),
  }));

  const semantic = [...scored].sort((a, b) => b.similarity - a.similarity);
  const lexical = [...scored].sort((a, b) => b.lexical - a.lexical);
  const fused = fuse(semantic.map((s) => s.key), lexical.map((s) => s.key));

  const target = scored.find((s) => s.key === open.expectKey);
  const semanticRank = semantic.findIndex((s) => s.key === open.expectKey) + 1;
  const lexicalRank = lexical.findIndex((s) => s.key === open.expectKey) + 1;
  const fusedRank = fused.indexOf(open.expectKey) + 1;
  const runnerUp = semantic.find((s) => s.key !== open.expectKey);

  const passes = fusedRank === 1 && target.similarity >= MIN_SIMILARITY;
  results.push({ passes, open, target, semanticRank, lexicalRank, fusedRank, runnerUp });

  console.log(`\n${passes ? "\x1b[32mPASS\x1b[0m" : "\x1b[31mFAIL\x1b[0m"}  ${open.title}`);
  console.log(`      expected: ${open.expect}`);
  console.log(`      similarity ${target.similarity.toFixed(3)}` +
    `  ·  rank: semantic #${semanticRank}, keyword #${lexicalRank}, fused #${fusedRank}`);
  console.log(`      margin over next-best (${runnerUp.key}): ` +
    `${(target.similarity - runnerUp.similarity).toFixed(3)}`);
}

bar("Threshold calibration");

// Every open ticket against every archive entry: the true pairs must sit clearly above the rest,
// and MIN_SIMILARITY has to fall in the gap between the two populations.
const truePairs = results.map((r) => r.target.similarity);
const distractors = [];
for (const [index, open] of OPEN.entries()) {
  RESOLVED.forEach((resolved, position) => {
    if (resolved.key !== open.expectKey) {
      distractors.push(cosine(queryVectors[index], archiveVectors[position]));
    }
  });
}

const stat = (xs) => ({
  min: Math.min(...xs), max: Math.max(...xs),
  mean: xs.reduce((a, b) => a + b, 0) / xs.length,
});
const t = stat(truePairs), d = stat(distractors);

console.log(`  true matches  (${truePairs.length}):  min ${t.min.toFixed(3)}  mean ${t.mean.toFixed(3)}  max ${t.max.toFixed(3)}`);
console.log(`  distractors   (${distractors.length}):  min ${d.min.toFixed(3)}  mean ${d.mean.toFixed(3)}  max ${d.max.toFixed(3)}`);
console.log(`  threshold ${MIN_SIMILARITY}: ` +
  `${truePairs.filter((s) => s >= MIN_SIMILARITY).length}/${truePairs.length} true matches kept, ` +
  `${distractors.filter((s) => s >= MIN_SIMILARITY).length}/${distractors.length} distractors let through`);

const separation = t.min - d.max;
console.log(`  separation (worst true − best distractor): ${separation.toFixed(3)}` +
  `${separation > 0 ? "  — the two populations do not overlap" : "  — \x1b[33mthey overlap; ranking, not the threshold, is doing the work\x1b[0m"}`);

bar("Verdict");
const passed = results.filter((r) => r.passes).length;
console.log(`  ${passed}/${results.length} paraphrased tickets retrieve their true counterpart at rank 1`);

if (t.min < MIN_SIMILARITY) {
  console.log(`  \x1b[31mMIN_SIMILARITY is too high\x1b[0m — the weakest true match (${t.min.toFixed(3)}) would be discarded.`);
} else if (t.min - MIN_SIMILARITY < 0.05) {
  console.log(`  \x1b[33mMIN_SIMILARITY has little headroom\x1b[0m — the weakest true match clears it by only ${(t.min - MIN_SIMILARITY).toFixed(3)}.`);
} else {
  console.log(`  MIN_SIMILARITY has ${(t.min - MIN_SIMILARITY).toFixed(3)} of headroom below the weakest true match.`);
}

const keywordOnlyMisses = results.filter((r) => r.lexicalRank !== 1);
console.log(`  term-overlap alone misses rank-1 on ${keywordOnlyMisses.length}/${results.length}` +
  ` — that gap is what the semantic half is for.`);
for (const miss of keywordOnlyMisses) {
  console.log(`    · "${miss.open.title}" — keyword #${miss.lexicalRank}, semantic #${miss.semanticRank}`);
}
if (keywordOnlyMisses.length === 0) {
  console.log(`    \x1b[33mNote:\x1b[0m every query also succeeds on term overlap, so this dataset does ` +
    `not yet show what semantic search adds. Reword an open ticket further from its counterpart.`);
}
console.log(`\n  (term overlap here approximates ts_rank_cd; Postgres weights rarer terms higher, so ` +
  `treat the keyword column as indicative, not exact.)`);

process.exit(passed === results.length ? 0 : 1);
