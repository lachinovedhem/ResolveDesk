# ResolveDesk — Developer Təlimatı

**Layihə:** ResolveDesk
**Sonuncu yeniləmə:** 2026-09-06

---

## 1. Ümumi arxitektura

```
  Browser (React SPA) ──► ResolveDesk.WebApi      (Native AOT, Minimal API)
                                 │
                                 ▼
                          ResolveDesk.Application  (portlar — asılılıqsız)
                                 │
                                 ▼
                          ResolveDesk.Infrastructure
                          ├── Dapper.AOT ──► PostgreSQL (+ pgvector)
                          ├── AiChatClient / AiEmbeddingClient ──► model
                          └── Local | LDAP identity, backfill worker

  MCP client ──► resolvedesk-mcp (stdio) ──► WebApi (HTTP)
                 └── Semantic Kernel triage agenti
```

Asılılıqlar yalnız içəri yönəlir. `Core` — təmiz record-lar; `Application` — portlar və DTO-lar;
`Infrastructure` — PostgreSQL, HTTP və directory serveri bilən yeganə layihə.

## 2. Texnologiya stack-i

- **API:** .NET 10, Native AOT, Minimal API (`CreateSlimBuilder`), source-generated JSON, Serilog, OpenAPI
- **Data:** PostgreSQL + Dapper.AOT, pgvector (HNSW, `vector_cosine_ops`)
- **AI:** öz HTTP klientlərimiz — Ollama / OpenAI / Azure OpenAI / Gemini / OpenAI-uyğun hər şey
- **MCP:** `ModelContextProtocol` 2.2, `Microsoft.SemanticKernel` 1.80 (bu layihə AOT **deyil**)
- **Web:** React 19, Vite, Tailwind, AG Grid, lucide-react

## 3. Verilənlər bazası

Connection string: `DB_CONNECTION_STRING` (env / secret store — heç vaxt fayla yazılmır).

| Cədvəl | Təyinat |
|---|---|
| `users` | Komanda; `password_hash` yalnız Local provayder üçün (LDAP/OIDC-də NULL) |
| `tickets` | Növbə; `reference` `ticket_ref_seq`-dən `INSERT` daxilində yaradılır |
| `ticket_activities` | Xronologiya: şərh, status dəyişikliyi, təyinat, həll |
| `ticket_embeddings` | `vector(N)` + `content_hash` + `model`; HNSW indeksi |

`DbSchema.EnsureAsync` **yalnız dev üçündür**. Produksiyada versiyalı miqrasiya (DbUp/Flyway) —
standards 02 §5.

## 4. Axtarış qatı — necə işləyir

```
yeni task ──► embed (N ölçü) ──► pgvector:  ORDER BY embedding <=> query   ──┐
          └─► plainto_tsquery ──► ts_rank_cd(title+description+resolution) ──┤
                                                                            ▼
                                        Reciprocal Rank Fusion (k = 60)
                                                                            ▼
                                        top N həll ──► model cavab layihəsi
```

Cosine similarity ilə `ts_rank` eyni şkalada olmadığı üçün skorlar toplanmır — RRF yalnız sıralamadan
istifadə edir. Cavabdakı `strategy` sahəsi hansı yolun işlədiyini bildirir.

**Deqradasiya:** embedding modeli yoxdursa → leksik; pgvector yoxdursa → leksik; chat modeli yoxdursa →
uyğunluqlar cavabsız qaytarılır; heç nə yoxdursa → məhsul yenə işləyir.

## 5. Layihə strukturu

```
src/ResolveDesk.Core            domen record-ları
src/ResolveDesk.Application     portlar, DTO-lar, AiOptions/AuthOptions
src/ResolveDesk.Infrastructure  Dapper.AOT, pgvector, AI klientləri, auth, backfill
src/ResolveDesk.WebApi          Minimal API (Native AOT)
src/ResolveDesk.Mcp             MCP serveri + Semantic Kernel triage agenti
frontend/                       React SPA
tools/mcp-smoke.mjs             MCP protokol testi
tools/seed-demo.mjs             Mock arxiv + təklif axını nümayişi
tools/demo.ps1                  DB yarat → API qaldır → seed → demo (tək əmr)
```

## 6. API endpointləri

```
POST   /api/v1/auth/login              GET  /api/v1/auth/config   GET /api/v1/auth/me
POST   /api/v1/auth/password
POST   /api/v1/tickets                 GET  /api/v1/tickets       GET /api/v1/tickets/{id}
POST   /api/v1/tickets/{id}/assign     POST /api/v1/tickets/{id}/status
GET    /api/v1/tickets/{id}/activities POST /api/v1/tickets/{id}/comments
GET    /api/v1/tickets/{id}/suggestions
POST   /api/v1/knowledge/search        GET  /api/v1/knowledge/stats
GET    /api/v1/users                   POST /api/v1/users
GET    /api/v1/stats                   GET  /api/v1/ai/status
GET    /health/live                    GET  /health/ready         GET /openapi/v1.json
```

## 7. AOT qaydaları (bu layihədə məcburi)

- Anonim tip **serializasiya etmə** — `AppJsonContext`-ə record əlavə et.
- `Results.Json<T>(value)` işlətmə — `JsonTypeInfo` overload-unu işlət
  (`Results.Json(value, AppJsonContext.Default.X, statusCode: …)`).
- Reflection-la options binding yoxdur — `AiOptions.Read` / `AuthOptions.Read` açarları açıq oxuyur.
- JWT bearer AOT-da dəstəklənən yeganə auth handler-dir; başqasını əlavə etmə.
- Build **0 xəbərdarlıq** olmalıdır. Analizator hər build-də işləyir; xəbərdarlıq = pozuntu.

## 8. Deploy və konfiqurasiya

```bash
dotnet publish src/ResolveDesk.WebApi -c Release -p:PublishAot=true
```
Windows-da Visual Studio "Desktop development with C++" (link.exe) tələb olunur.

## 9. Lokal işə salma

```powershell
$env:PGPASSWORD = '<postgres parolu>'
./tools/demo.ps1                  # DB + API + seed + demo

# yaxud ayrı-ayrı:
dotnet run --project src/ResolveDesk.WebApi --no-launch-profile
cd frontend; npm run dev
node tools/mcp-smoke.mjs
```
