# Yol Xəritəsi (PLAN)

## Ümumi Məqsəd
Call-center texniki komandası üçün task idarəetmə sistemi. Fərqləndirici cəhət: hər yeni task üçün
cavab komandanın **öz keçmiş həllərindən** tapılır — hibrid axtarış (pgvector + full-text) və model
tərəfindən həmin həllərə əsaslanan cavab layihəsi.

## Mərhələ 1 — Skelet ✅
- [x] Clean Architecture + Native AOT + health + OpenAPI + Serilog

## Mərhələ 2 — Task domeni ✅
- [x] `Ticket` / `User` / `TicketActivity`, status–prioritet–mənbə enum-ları
- [x] Dapper.AOT repoları: keyset paginasiya, null-qorumalı filtrlər
- [x] Referans (`RD-2026-000123`) `INSERT` daxilində sequence-dən — yarış yoxdur
- [x] 11 endpoint + SLA hesablanması (Urgent 4s / High 8s / Normal 24s / Low 72s)

## Mərhələ 3 — AI qatı ✅
- [x] `IAiChatClient` / `IAiEmbeddingClient` — chat və embedding **ayrıca** konfiqurasiya
- [x] Ollama · OpenAI · Azure OpenAI · Gemini · hər OpenAI-uyğun server
- [x] Söndürülmüş rejim (`None`) — məhsul AI olmadan da tam işləyir
- [x] `/api/v1/ai/status` — provayder, model, əlçatanlıq (Ollama-da **model** yoxlanılır), indeks əhatəsi

## Mərhələ 4 — Semantik indeks ✅
- [x] `ticket_embeddings` + HNSW (`vector_cosine_ops`), ölçü konfiqurasiyadan
- [x] pgvector yoxdursa → yalnız açar söz axtarışı (deqradasiya, xəta yox)
- [x] `PgVectorIndex` — cosine axtarış, content-hash ilə köhnəlmiş sətirlərin tapılması

## Mərhələ 5 — Təklif axını (RAG) ✅
- [x] `SuggestionService`: embed → vektor axtarış ⊕ full-text → RRF (k=60) → model cavabı
- [x] `EmbeddingBackfillService` — arxa fonda, idempotent, model dəyişəndə özü doldurur
- [x] Prompt injection: task mətni və keçmiş həllər **məlumatdır, göstəriş deyil** (system prompt-da açıq)

## Mərhələ 6 — Daxili MCP serveri ✅
- [x] `resolvedesk-mcp`, stdio, 10 alət
- [x] `triage_ticket` — Semantic Kernel agenti, function calling, yalnız oxuyan plugin
- [x] `tools/mcp-smoke.mjs` — protokol və alət səthi testi

## Mərhələ 7 — Autentifikasiya ✅
- [x] Local (PBKDF2-SHA256, 210k iterasiya) · LDAP/Active Directory · OIDC
- [x] JWT buraxılışı hər provayder üçün eyni; rol siyasətləri (coordinator / admin)
- [x] Login rate limit (IP başına 10/dəq), hesab enumerasiyası yoxdur, default hesab yoxdur

## Mərhələ 8 — Frontend ✅
- [x] Design tokenləri (`tokens.css` + `tailwind.config.ts`) — kodda hex yoxdur
- [x] ≥1024px AG Grid (virtual, floating filter, paginasiya yox) / <1024px tile card, lazy
- [x] Dark mode (light/dark/system), PWA + quraşdır düyməsi, az/en, skeleton, a11y

## Mərhələ 9 — Paketləmə ✅
- [x] `docker-compose.yml` (pgvector/pg17 + opsional Ollama), `.env.example`, README

## Mərhələ 10 — AI bütün axına ✅
- [x] **Qiymətləndirmə** (`TicketAssessment`): çətinlik 1–5, dəqiqə təxmini, kateqoriya, prioritet,
      **dublikat aşkarlama** (eyni müştəri + mətnən yaxın açıq tasklar), əminlik göstərilir
- [x] **Marşrutlaşdırma** (`RoutingService`) — **model yox, arifmetika**:
      `0.5·oxşarı həll edib + 0.3·bacarıq uyğunluğu + 0.2·boş yer`; hər üç rəqəm ekranda
- [x] **Həll baxışı** (`ResolutionReview`): müştəriyə hazır cavab + daxili xülasə + əvvəlki
      praktika ilə müqayisə (Consistent / Differs / Novel / **Conflicts** → koordinatora bildiriş)
- [x] **Bildirişlər**: PostgreSQL-də davamlı + SSE ilə canlı + brauzer bildirişi + PWA nişanı
- [x] **SLA gözətçisi**: SLA-ya az qalmış tasklar üçün bir dəfəlik xəbərdarlıq (idempotent)
- [x] **Triage növbəsi**: model çağırışları request yolundan çıxarılıb — task yaratmaq və həll etmək
      dərhal cavab verir; model düşsə yazı uğursuz olmur
- [x] Mobil: alt tab paneli (safe-area), 44px toxunuş hədəfləri

## Backlog
- [ ] **DB üzərində real test** (bloklanıb — bax STATE §1)
- [ ] Testcontainers ilə inteqrasiya testləri (repo + hibrid axtarış + auth)
- [ ] Versiyalı miqrasiyalar (DbUp/Flyway) — `DbSchema` yalnız dev üçündür (standards 02 §5)
- [ ] SSE ilə canlı növbə yeniləmələri (standards 12)
- [ ] Native AOT publish yoxlaması (Windows-da Visual C++ `link.exe` tələb olunur)
- [ ] Reranker modeli (cross-encoder) — RRF-dən sonra ikinci sıralama
