# Cari Vəziyyət (STATE) — indeks
- Son yenilənmə: 2026-09-06

## Tamamlanan mərhələlər
| ID | Başlıq | Status | Yoxlanılıb |
|----|--------|--------|------------|
| T-001 | Skelet (Clean Architecture + AOT + health + OpenAPI) | done | build 0/0 |
| T-002 | Task domeni: Ticket/User/Activity, Dapper.AOT repoları, 11 endpoint | done | build 0/0 |
| T-003 | AI qatı: Ollama / OpenAI / Azure / Gemini, konfiqurasiyadan seçilir | done | canlı `/ai/status` |
| T-004 | pgvector semantik indeks + hibrid axtarış (RRF) | done | build; DB testi gözləyir |
| T-005 | Təklif axını (RAG) + arxa fon embedding backfill | done | build; DB testi gözləyir |
| T-006 | MCP serveri (10 alət) + Semantic Kernel triage agenti | done | smoke 5/5 |
| T-007 | Auth: Local (PBKDF2) / LDAP-AD / OIDC, rate limit, rol siyasətləri | done | build 0/0 |
| T-008 | Frontend: React+Vite+Tailwind, AG Grid/tile, PWA, dark mode, az/en | done | `npm run build` |
| T-009 | docker-compose (pgvector, ollama), .env.example, README | done | — |
| T-010 | Qiymətləndirmə, marşrutlaşdırma, həll baxışı, bildirişlər (SSE), SLA gözətçisi | done | build 0/0, tsc OK |

## Yoxlama nəticələri
- `dotnet build ResolveDesk.slnx --no-incremental` → **0 warning, 0 error** (AOT analizatoru aktiv)
- `node tools/mcp-smoke.mjs` → **5/5** (10 alət, DI parametrləri schema-da görünmür)
- `npm run build` (frontend) → uğurlu; grid ayrıca chunk (1 034 kB / gzip 290 kB)
- Canlı API: `/health/live` 200 · `/api/v1/auth/config` 200 · `/api/v1/ai/status` 200
- Ollama wire formatı birbaşa yoxlanıldı: `/api/chat` uyğun gəlir

## Retrieval kalibrasiyası (ölçülüb, 2026-09-06)
`node tools/validate-retrieval.mjs` — baza tələb etmir, yalnız Ollama.
- **7/7** paraphrase edilmiş task öz doğru qarşılığını 1-ci sırada tapır
- Doğru uyğunluqlar 0.582–0.740 · yanlışlar 0.332–0.630 → **populyasiyalar üst-üstə düşür**
- `MinSimilarity` 0.35 → **0.50** (bax ADR-011); artıq konfiqurasiyadadır (`Ai:MinSimilarity`)
- Harness-in leksik sütunu **kobud yaxınlaşdırmadır** (termin üst-üstə düşməsi) və 7-dən 3-ünü
  qaçırır. Real PostgreSQL `ts_rank_cd` (normalizasiya 36) isə **7/7** verir — yəni yaxınlaşdırma
  bazanı olduğundan zəif göstərirdi. Bax ADR-014.

## Real bazada işə salındı (2026-09-06)
`resolvedesk` bazası yaradıldı, sxem quruldu, arxiv əkildi, demo işlədi. Yalnız işə salanda üzə çıxan
**dörd baq** tapıldı və düzəldildi — build hamısında təmiz idi (0 xəbərdarlıq):
| # | Baq | ADR |
|---|-----|-----|
| 1 | `DateTimeOffset` ↔ Npgsql `timestamptz` uyğunsuzluğu → `GET /users` 500 | ADR-013 |
| 2 | `plainto_tsquery` AND yaradır → axtarış 0 nəticə | ADR-014 |
| 3 | `ts_rank_cd * 10` doyur → bütün ballar 100%, sıralama tarixə düşür | ADR-014 |
| 4 | Demo qiymətləndirmə üçün 30 s gözləyirdi; CPU-da model daha uzun çəkir | — |
| 5 | `AssessAsync` lazımsız yerə müştəri cavabı yazdırırdı → hər qiymətləndirmə 2× yavaş | — |
| 6 | Tarixi arxivin idxalı 10 lazımsız "həll baxışı" model çağırışı yaradırdı | — |

## pgvector quraşdırıldı (2026-09-06)
Rəsmi mənbədən qurulub: `github.com/pgvector/pgvector` teq **v0.8.6**, MSVC (VS 18) + `nmake /F
Makefile.win`. Üçüncü tərəf binar yüklənməyib. Geri qaytarmaq: `libector.dll` və
`share\extensionector*` silmək.
- `vectorSearchAvailable: true` · HNSW indeksi `vector_cosine_ops` ilə
- Backfill işçisi **11/11** taskı özü indeksləşdirdi (768 ölçü) — ADR-004-ün iddiası təsdiqləndi

## Canlı demo: hibrid axtarış (pgvector ilə)
- **7/7** açıq task doğru arxiv qarşılığını **1-ci sırada** tapdı
- Açar sözün qaçırdığı yeganə hal (RD-000017, CGNAT) həll olundu: **59% vs 57%**.
  Offline harness bunu 0.594 vs 0.570 kimi proqnozlaşdırmışdı — canlı nəticə uyğun gəldi.
- `strategy: hybrid` — nəticələrin çoxu hər iki siyahıda görünür, RRF onları yuxarı qaldırır

## Əvvəlki demo nəticəsi (yalnız açar söz, müqayisə üçün)
- **6/7** açıq task öz doğru arxiv qarşılığını **1-ci sırada** tapdı
- Qaçırılan yeganə hal — RD-000017: `0.091` **bərabərlik**, tie-break tarixə düşdü.
  Məhz burada semantik axtarış həll edərdi (offline ölçmədə cosine 0.594 vs 0.570 — real fərq).
- Cavablar arxivə **əsaslanır**: model RD-2026-000001-i istinad edib SNR/17a→8b/SRA addımlarını verdi
- Qiymətləndirmə: 5/7 demo pəncərəsində yetişdi (2-si növbədə qaldı — CPU modeli yavaşdır)
- Həll baxışı: `Consistent` hökmü + müştəriyə göndərilə bilən cavab + daxili xülasə

## Bloklananlar / Açıq suallar
1. ~~PostgreSQL parolu~~ → **həll olundu**, baza işləyir.
2. **pgvector hələ də quraşdırılmayıb.** `share\extension\vector.control` və `lib\vector.dll` yoxdur.
   MSVC C++ toolchain (VS 18 Enterprise) və git **mövcuddur** → rəsmi mənbədən qurula bilər
   (üçüncü tərəf binar yükləmədən). Onsuz sistem açar söz axtarışına deqradasiya edir — sınmır.
3. ~~Testlər yazılmayıb~~ → **yazıldı**: 70 test (47 vahid + 23 baza). Bax aşağıda.

## ✅ Həll olundu
- ~~`nomic-embed-text` çəkilməyib~~ → çəkildi. 768 ölçü, konfiqurasiya ilə dəqiq uyğun.
  `/ai/status`: `embeddingReachable: true`.

## Testlər (2026-09-06)
`tests/ResolveDesk.Tests` — **70 test, hamısı keçir** (bazasız: 47 keçir, 22 **skip** kimi bildirilir).
- Bağlantı sətri `RESOLVEDESK_TEST_DB`-dədir; fixture `resolvedesk_test` bazasını yaradıb silir və
  sxemi tətbiqin öz `DbSchema`-sı ilə qurur (kopyalanmış DDL sürüşməsin deyə).
- **Testcontainers istifadə edilmədi** — bu maşında Docker yoxdur, yəni yoxlaya bilməzdim.
  Sınanmamış kod göndərmək əvəzinə bağlantı sətri yanaşması seçildi; CI-da `pgvector/pgvector`
  servis konteyneri ilə eyni işləyir (`.github/workflows/ci.yml`).
- CI skip sayı 0 deyilsə build-i sındırır — əks halda heç nə işlətməyən yaşıl run əsl runa oxşayır.
- **Mutasiya ilə yoxlanılıb:** hər reqressiya testi düzəlişi geri qaytarıb işlədilib, yalnız uğursuz
  olduqdan sonra saxlanılıb. İkisi ilk cəhddə tutmadı (biri boş assertion, biri səhv test datası) —
  hər ikisi yenidən yazıldı.

## UI brauzerdə işlədildi (2026-09-06)
API :8080, frontend :5173, hər ikisi real data ilə. Konsolda xəta yoxdur.
- Dashboard, AG Grid (18 task, sütun filtrləri), task detalı, mobil tile kartlar — hamısı işləyir
- Task RD-000017-də hibrid axtarış CGNAT taskını **59%** ilə birinci qoyur, nişan "meaning + keyword"
- AI cavabı RD-2026-000008-ə istinad edir; marşrutlaşdırma izahını göstərir
- **İki UI baqı tapıldı və düzəldildi** (bax ADR-016): "Due in in 7 hours"; və azərbaycanca
  `Intl.RelativeTimeFormat` tərcümə etmir → "+7 h qalıb". İkincisi yalnız brauzerdə, yalnız az-da görünür.
- Qeyd: təklif paneli lokal CPU modelində ~50 s çəkir (draft cavab). Hosted provayderdə saniyələrlə olur.

## Açıq qalan
- Frontend-də test harness yoxdur (vitest qurulmayıb) — ADR-016-dakı iki baq məhz belə testlə tutulardı.

## Bu sessiyada əlavə edilənlər (2026-09-06, davam)
| Sahə | Nəticə |
|------|--------|
| Marşrutlaşdırma | 13 s → **2 ms** (saxlanılır); "Yenidən" düyməsi + təhlil vaxtı. ADR-017 |
| Light mode | `theme-color` sabit tünd idi → mobil/PWA xromu light-da da tünd qalırdı. Düzəldildi. |
| Giriş | Parol + LDAP/AD + OIDC + **passkey** + **TOTP**, hamısı eyni anda. ADR-018 |
| Hesab yaratma | Öz-özünə qeydiyyat yoxdur; **birdəfəlik keçid**, açılanda yanır. |
| Test | `tools/auth-smoke.mjs` — **42/42** |

## Açıq qalan
- Frontend-də test harness yoxdur (vitest qurulmayıb).
- ~~Handle-lar yaddaşdadır~~ → **paylaşılan saxlamaya köçürüldü** (ADR-020), `tools/multi-instance-smoke.mjs` 15/15.
- ~~OIDC `/auth/oidc/start` yoxdur~~ → **tamamlandı** (ADR-019). PKCE + JWKS + nonce, öz tokenimiz,
  birdəfəlik ötürmə kodu. `tools/oidc-smoke.mjs` **25/25**, brauzerdə də sınandı.

## OIDC tamamlandı (2026-09-06)
- Server tərəfdə authorization code + **PKCE (S256)**; ID token provayderin JWKS-i ilə yoxlanılır
- API **öz tokenini** verir → SSO parol və passkey ilə yan-yana işləyir, sessiya formatı eynidir
- Rol provayderin iddiasından xəritələnir (`admins` → Admin, canlı yoxlanıldı)
- Sessiya son yönləndirməni **60 saniyəlik birdəfəlik kodla** keçir, token URL-ə düşmür
- İki forma: `ClientId` varsa interaktiv, yoxsa köhnə passthrough — startup hansının işlədiyini yazır
- Test alətləri: `tools/fake-oidc-provider.mjs` (real RSA, PKCE məcburi) + `tools/oidc-smoke.mjs`
- Brauzerdə tapılan baq: StrictMode effekti iki dəfə işlədirdi, callback səhifəsi uğurlu girişin
  üstündən "keçid natamam" yazırdı. Düzəldildi (ADR-019).

## Paylaşılan vəziyyət (2026-09-06)
Dörd axının handle-ları `auth_handles` cədvəlindədir: MFA, SSO ötürmə, passkey challenge, OIDC state.
- `DELETE … RETURNING` → atomar istifadə; yarışan iki nüsxədən yalnız biri udur
- Yalnız SHA-256 hash saxlanılır — baza dump-ı işlək handle vermir
- `purpose` sorğunun bir hissəsidir → bir axının handle-ı digərində işləmir
- Yoxlama: **15/15** iki nüsxə arasında (A-da başla, B-də bitir)
- Kodda `ConcurrentDictionary`/`static Dictionary` qalmayıb — yoxlanılıb

## Açıq qalan
- Frontend-də test harness yoxdur (vitest qurulmayıb).
- **Sürət limiti proses daxilindədir** — N nüsxə N dəfə çox cəhdə icazə verir. Bazaya köçürmək sorğu
  başına yazma deməkdir; düzgün yer balanslayıcı, yaxud Redis-dir (ADR-020).
