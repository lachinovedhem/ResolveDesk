# İş Jurnalı (JOURNAL)
> Ən yeni yuxarıda.

## 2026-09-06 — Faza 10: AI bütün axına + bildirişlər
**Görülən iş:**
- **Qiymətləndirmə**: çətinlik 1–5, dəqiqə təxmini, kateqoriya/prioritet, dublikat aşkarlama.
  Arxa fon növbəsində işləyir — task yaratmaq modeli gözləmir.
- **Marşrutlaşdırma**: modelə deyil, üç ölçülə bilən siqnala əsaslanır; rəqəmlər ekranda (ADR-008).
- **Həll baxışı**: müştəriyə hazır cavab + daxili xülasə + əvvəlki praktika ilə müqayisə.
  `Conflicts` verdikti koordinatora bildiriş göndərir. Əməkdaşın öz mətni **dəyişdirilmir**.
- **Bildirişlər**: bazada davamlı, SSE ilə canlı, brauzer bildirişi, PWA nişanı (ADR-010).
- **SLA gözətçisi** + mobil alt tab paneli.

**Qərar nöqtələri:**
- `ITriageQueue` əvvəlcə Infrastructure-də idi → RequestDelegateGenerator onu görmədi. Port kimi
  Application-a köçürüldü (memarlıq baxımından da düzgün yer).
- `TicketDetail.tsx`-də lokal `RoutingCard` yeni idxal ilə ad toqquşması yaratdı → `AssignmentCard`.
- Model JSON-u üçün mötərizə uyğunlaşdıran çıxarıcı yazıldı (ADR-009) — lokal modellər təmiz JSON
  qaytarmır.

**Yoxlanıldı:** build 0/0 (non-incremental) · MCP smoke 5/5 · `tsc --noEmit` təmiz · frontend build ·
canlı `/health/live`, `/ai/status`. Data endpoint-ləri DB olmadan 500 verir — bu düzgün davranışdır.

## 2026-09-06 — Faza 3–9: AI, pgvector, MCP, auth, frontend
**Görülən iş:**
- **AI qatı**: chat və embedding ayrıca konfiqurasiya olunan provayderlər (Ollama / OpenAI / Azure /
  Gemini / OpenAI-uyğun hər şey). Öz HTTP klientimiz + source-generated JSON — AOT təmiz qaldı.
- **pgvector**: `ticket_embeddings` + HNSW; extension yoxdursa açar söz axtarışına deqradasiya.
- **RAG**: `SuggestionService` — vektor ⊕ full-text, RRF (k=60) ilə birləşdirmə, sonra model cavabı.
  Arxa fon backfill content-hash ilə idempotentdir.
- **MCP**: `resolvedesk-mcp` (10 alət) + Semantic Kernel `triage_ticket` agenti.
- **Auth**: Local / LDAP-AD / OIDC — yalnız credential yoxlaması dəyişir.
- **Frontend**: React 19 + Vite + Tailwind, tokenlər, AG Grid/tile card, PWA, dark mode, az/en.

**Yol boyu düzəldilənlər:**
- `Results.Json<T>` reflection overload-u AOT analizatoru tərəfindən tutuldu → `JsonTypeInfo`
  overload-u ilə əvəzləndi (IL2026/IL3050 getdi).
- `IssuerSigningKeyResolver` içində `BuildServiceProvider()` ikinci konteyner yaradırdı → issuer
  `Program.cs`-də bir dəfə qurulur, açar birbaşa paylaşılır.
- AG Grid `Tickets.tsx`-də statik import idi → mobil də 1 MB yükləyirdi; `React.lazy` ilə ayrıldı.
- `/ai/status` DB düşəndə 500 verirdi → `StatsAsync` deqradasiya edir (diaqnostika endpoint-idir).
- Ollama probe-u yalnız daemon-u yoxlayırdı → indi **konfiqurasiya edilmiş modelin** olub-olmadığını
  yoxlayır. Bu, `nomic-embed-text` çəkilmədiyini düzgün göstərdi.
- Token faylındakı kontrast iddiası səhv idi (4.62:1 yazılmışdı) → ölçüldü: light 4.90:1, dark 5.30:1.

**Yoxlanıldı:** build 0/0 (non-incremental) · MCP smoke 5/5 · frontend build · canlı
`/health/live`, `/auth/config`, `/ai/status` · Ollama `/api/chat` wire formatı.

**Növbəti addım:** PostgreSQL bağlantısı (STATE §1) və `ollama pull nomic-embed-text` (STATE §2) —
hər ikisi istifadəçinin qərarını gözləyir. Sonra: real datada RAG axını + Testcontainers testləri.

## 2026-09-06 — Bootstrap
**Görülən iş:** Standartlara uyğun .NET skeleti quruldu (bootstrap script).
**Növbəti addım:** Real domen + endpointlər.
