# Qərarlar Jurnalı (DECISIONS / ADR)

> **Bu, işçi qeyddir (azərbaycanca).** Oxucular üçün nəzərdə tutulmuş, yenilənən variant:
> [`docs/decisions.md`](../docs/decisions.md) — ingiliscə. İkisi fərqlənərsə, ingiliscə olan doğrudur.

## ADR-001 — Platforma və memarlıq
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Yeni .NET servisi.
- Qərar: Clean Architecture + Native AOT + Dapper.AOT + Minimal API (standards 01, 02).
- Nəticə: DB = PostgreSQL; auth opsional (standards 06).


## ADR-002 — AI provayderləri: öz HTTP qatımız, Microsoft.Extensions.AI deyil
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: API Native AOT-dur. Bir neçə provayder (lokal + hosted) konfiqurasiyadan seçilməlidir.
- Qərar: Hər provayder üçün nazik HTTP klienti + source-generated JSON (`AiJsonContext`).
  `OpenAi` provayderi bütün OpenAI-uyğun serverləri (LM Studio, vLLM, llama.cpp, OpenRouter) əhatə edir.
- Səbəb: `Microsoft.Extensions.AI` və OpenAI SDK-nın trimming/AOT xəbərdarlıqları var; wire formatlar isə
  sadədir (3 endpoint şəkli). Nəticədə build 0 xəbərdarlıq verir.
- Nəticə: Yeni provayder = bir `switch` qolu + bir wire record. Chat və embedding **ayrı** konfiqurasiya
  olunur — biri lokal, digəri hosted ola bilər.


## ADR-003 — Hibrid axtarış (pgvector + full-text), RRF ilə birləşdirilir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Məhsulun əsas funksiyası — yeni task üçün keçmiş həlləri tapmaq.
- Qərar: Semantik (cosine, HNSW) + leksik (`ts_rank_cd`) nəticələr **Reciprocal Rank Fusion** (k=60) ilə
  birləşdirilir.
- Səbəb: Cosine similarity ilə `ts_rank` eyni şkalada deyil — birbaşa toplamaq olmaz. RRF yalnız
  sıralamadan istifadə edir. Hər iki siyahıda görünən task yuxarı qalxır.
- Nəticə: `strategy` sahəsi hansı yolun işlədiyini bildirir; pgvector və ya model olmadan da məhsul işləyir.


## ADR-004 — Embedding-lər arxa fonda, content hash ilə
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Qərar: `EmbeddingBackfillService` (BackgroundService) `md5(title+description+resolution)` ilə
  köhnəlmiş sətirləri tapıb yenidən embed edir.
- Səbəb: Task bağlamaq modeli gözləməməlidir; sonradan qurulan indeks özü doldurulmalıdır; düzəliş
  edilmiş həll yenidən embed olunmalıdır. Hash bunların üçünü də bir sorğu ilə həll edir.


## ADR-005 — MCP serveri ayrıca layihə, AOT deyil; Semantic Kernel orada
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Semantic Kernel function calling üçün reflection istifadə edir → AOT ilə uyğun deyil.
- Qərar: `ResolveDesk.Mcp` ayrıca konsol layihəsidir (`PublishAot=false`), API ilə **HTTP üzərindən**
  danışır. Triage aləti SK agentidir.
- Səbəb: API hər sorğuya xidmət edir — AOT orada qazandırır. MCP isə qısa ömürlü stdio prosesidir.
  HTTP üzərindən getmək bir binarın istənilən instansiyaya (lokal/staging/prod) yönəlməsinə imkan verir
  və SQL-i təkrarlamır.


## ADR-006 — Auth: yalnız credential yoxlaması dəyişir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Qərar: `IIdentityValidator` (Local / LDAP) → hər ikisi eyni JWT-ni buraxır. OIDC-də SPA axını icra edir,
  API yalnız tokeni yoxlayır.
- Səbəb: JWT bearer Native AOT-da dəstəklənən **yeganə** authentication handler-dir ("Other
  Authentication ❌" — ASP.NET Core AOT uyğunluq cədvəli). Bu quruluş OIDC-ni də AOT sərhədində saxlayır.
- Nəticə: Local → LDAP keçidi bir konfiqurasiya dəyəridir. Default hesab **yoxdur**; ilk admin
  `Auth__BootstrapAdminEmail/Password` ilə bir dəfə yaradılır.


## ADR-007 — Frontend: AG Grid lazy, tile card şərti render
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Qərar: ≥1024px AG Grid, <1024px tile card — CSS ilə gizlətmə yox, şərti render + `React.lazy`.
- Səbəb: Gizlədilmiş grid yenə də ~1 MB bundle və DOM xərci deməkdir. Ölçü: grid chunk 1 034 kB
  (gzip 290 kB) ayrıca yüklənir; mobil giriş 247 kB + 50 kB.


## ADR-008 — Marşrutlaşdırma modelə deyil, arifmetikaya əsaslanır
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: "Kim baxsın" sualı LLM-ə verilə bilərdi.
- Qərar: Üç ölçülə bilən siqnal — kim oxşar taskları həll edib (0.5), bacarıq uyğunluğu (0.3),
  boş yer (0.2). Hər üç rəqəm namizədin yanında göstərilir.
- Səbəb: Koordinator rəqəmləri görəndə tövsiyəni **etirazla** qəbul edir — modelin "hökmü" isə ya
  kor-koranə qəbul olunur, ya da tamamilə göz ardı edilir. Üstəlik arifmetika test edilə bilər.
  Modelin payı yuxarı axındadır: çətinlik və kateqoriya.
- Nəticə: Heç bir siqnal yoxdursa, səbəb açıq deyir: "təcrübə və bacarıq uyğunluğu yoxdur, sadəcə
  növbəsi ən boşdur" — "ən az yüklü"-nü uyğunluq kimi təqdim etmir.


## ADR-009 — Model çıxışı struktur data kimi etibarlı sayılmır
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Qiymətləndirmə və həll baxışı modeldən JSON tələb edir. Lokal modellər JSON-u ```-la
  bükür, qabağına mətn qoyur, sonuna nəzakət cümləsi əlavə edir.
- Qərar: `ModelJson.Extract` — sətir vəziyyətini nəzərə alan mötərizə uyğunlaşdırması ilə ilk balanslı
  `{…}` çıxarılır; bütün sahələr nullable; rəqəmlər diapazona sıxılır (`Math.Clamp`).
- Səbəb: Təmiz cavab tələb etmək məhz bu məhsulun hədəf aldığı lokal quraşdırmada funksiyanı
  qeyri-sabit edərdi. Səhv `difficulty: 99` növbə sıralamasını zəhərləməməlidir.


## ADR-010 — Bildirişlər: SSE, EventSource yox
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Qərar: Bildirişlər PostgreSQL-də davamlıdır; canlı çatdırılma SSE ilədir. Klient `EventSource`
  deyil, `fetch` + `ReadableStream` istifadə edir.
- Səbəb: `EventSource` `Authorization` başlığı göndərə bilmir — alternativ tokeni URL-ə qoymaqdır,
  bu isə proxy loglarına və brauzer tarixçəsinə düşür. Yenidən qoşulma bizim üzərimizdədir
  (məhdudlaşdırılmış eksponensial gecikmə).
- Nəticə: Hub proses daxilindədir. Çoxnüsxəli quraşdırmada A node-una qoşulmuş klient B-dən gələn
  canlı push-u qaçırır və növbəti yükləmədə bazadan götürür — deqradasiya, itki yox. Redis pub/sub-a
  keçid bir sinif dəyişikliyidir.


## ADR-011 — MinSimilarity ölçüldü, ehtimal edilmədi; və konfiqurasiyaya köçürüldü
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: `MinSimilarity = 0.35` ədəbiyyatdan götürülmüşdü — yəni təxmin idi.
- Ölçmə: `tools/validate-retrieval.mjs` (baza tələb etmir, yalnız Ollama). Nəticə: 0.35-də 36 yanlış
  cütdən **35-i** eşiyi keçirdi — filtr praktiki olaraq işləmirdi. `nomic-embed-text` bal diapazonunu
  ~0.33–0.74 aralığına sıxır.
- Kritik tapıntı: **populyasiyalar üst-üstə düşür** — ən zəif doğru uyğunluq (0.582) ən güclü yanlışdan
  (0.630) aşağıdır. Deməli **heç bir eşik onları ayıra bilməz**. Ayırıcı işi sıralama görür; eşik yalnız
  aşağı həddi kəsir.
- Qərar: Default 0.50 (7/7 doğru saxlanır, 18/63 yanlış keçir, 0.082 ehtiyat). 0.55 cəmi 6 əlavə yanlış
  kəsir, amma ehtiyatı yarıya endirir — 7 sorğuluq nümunədə pis mübadilə.
- Nəticə: Dəyər `Ai:MinSimilarity`-yə köçürüldü, çünki miqyas **modelə** aiddir, məhsula yox.
  Model dəyişəndə harness yenidən işlədilməlidir.


## ADR-012 — Demo datası özünü təriflədiyi üçün dəyişdirildi
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: İlk 4 açıq task yalnız açar söz üstünlüyü ilə də 1-ci yerə düşürdü — yəni demo semantik
  axtarışın nə verdiyini sübut etmirdi, sadəcə təkrarlayırdı.
- Qərar: Müştərinin danışdığı kimi yazılmış, mühəndisin yazısı ilə demək olar heç bir söz paylaşmayan
  3 task əlavə edildi (*"səsim sanki su altındandır"* ↔ *"B ayağında izolyasiya müqaviməti aşağıdır —
  birləşmə qutusuna su düşüb"*).
- Nəticə: Termin üst-üstə düşməsi indi 7-dən **3-ündə** 1-ci yeri qaçırır; vektor tərəfi yeddisini də
  tapır. Bu fərq məhz funksiyanın özüdür.
- Əlavə: Korpus `tools/demo-corpus.mjs`-ə çıxarıldı — seeder və harness eyni datanı paylaşır, ayrıla bilmir.


## ADR-013 — Domen tipləri DateTimeOffset yox, DateTime (UTC) istifadə edir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Real PostgreSQL-ə qarşı ilk işə salmada `GET /users` 500 qaytardı:
  `InvalidCastException: Invalid cast from 'System.DateTime' to 'System.DateTimeOffset'`.
- Səbəb: Sütunlar düzgündür (`timestamptz`). Npgsql `timestamptz`-i **`DateTime(Kind=Utc)`** kimi qaytarır.
  Dapper.AOT isə `DateTimeOffset`-i öz generasiya etdiyi sürətli yolda tanımır — geri düşüb
  `Convert.ChangeType` çağırır, o da `DateTime → DateTimeOffset` çevrilməsini bacarmır.
- Vacib: bu **yalnız işə salanda** üzə çıxdı. Build təmiz idi, 0 xəbərdarlıq — kompilyator bunu tuta bilməz.
- Qərar: Bütün domen tipləri `DateTime` (UTC). `timestamptz` sütunu onsuz da məhz "UTC-də bir an"
  deməkdir — `...AtUtc` adları da bunu deyir, ona görə offset artıqdır.
- Əhatə: 23 yer / 14 fayl. JWT `exp` müddəti də dəyişdirildi ki, API-də **vahid konvensiya** olsun —
  əks halda login `+00:00`, tasklar isə `Z` qaytaracaqdı.
- Nəticə: JSON indi hər yerdə `"2026-09-06T18:33:38.273961Z"`. Frontend-də dəyişiklik lazım deyil.

## ADR-014 — Leksik axtarış: OR semantikası, generasiya olunan tsvector, ölçülmüş normalizasiya
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: Real bazada ilk demo **0 nəticə** verdi (`strategy: none`), sonra isə **hamısı 100%** və
  səhv sıralama. Üç ayrı baq idi:
- **1. `plainto_tsquery` AND yaradır.** 32 sözlük task mətni üçün namizəd *hər* sözü ehtiva etməlidir —
  praktiki olaraq heç vaxt. Düzəliş: mətn `to_tsvector`-dən keçirilir, leksemlər `|` ilə birləşdirilir.
  Bu həm də təmizləyir: müştərinin yazdığı nə olursa-olsun lekseme çevrilir, tsquery operatoruna yox.
  `NULLIF` bütün-stopword halını qoruyur (`to_tsquery('')` sintaksis xətası verir).
- **2. `ts_rank_cd(...) * 10.0` doyurdu.** `LEAST(1.0, ...)` ilə birlikdə hər bal 1.0-a bərabər olurdu,
  `ORDER BY` isə `resolved_at_utc`-a düşürdü — nəticələr tarixə görə sıralanırdı, uyğunluğa yox.
- **3. Normalizasiya seçilməmişdi.** Ölçdüm (7 sorğu, bilinən doğru cavab):
  `0,1,2,8,16,32,33` → 5–6/7 birinci sıra; **`4` və `36` (=4|32) → 7/7**.
  Bayraq 4 terminlərin bir-birinə **yaxınlığını** mükafatlandırır — "eyni problem, başqa sözlər" məhz
  belə görünür. Bayraq 32 monoton şəkildə [0,1)-ə salır, sıralamanı pozmur.
- Performans: `tickets.search_tsv` generasiya olunan STORED sütun + GIN indeksi. Əvvəl hər sorğu hər
  sətri yenidən tokenləşdirirdi. Dil `Search:TextSearchConfig`-dədir; generasiya olunan sütun yalnız
  immutable ifadə çağıra bildiyi üçün dəyər sxemə "bişirilir" — dəyişmək migrasiya tələb edir.
- Vacib nəticə: bu bal **relevantlıqdır, oxşarlıq deyil** — 0.04 qəti birinci yer ola bilər və cosine
  similarity ilə müqayisə edilə bilməz. Məhz buna görə RRF ballara yox, **sıralamaya** baxır (ADR-003).

## ADR-015 — pgvector rəsmi mənbədən qurulur, hazır binar yüklənmir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: PostgreSQL 18 Windows-da pgvector gətirmir. Variantlar: icma tərəfindən yığılmış DLL
  yükləmək, yaxud mənbədən qurmaq.
- Qərar: Mənbədən. `github.com/pgvector/pgvector`, **v0.8.6 buraxılış teqi** (master deyil),
  MSVC (VS 18) + `nmake /F Makefile.win`.
- Səbəb: Naməlum mənbədən binar götürüb `C:\Program Files`-a yerləşdirmək — serverin daxilində
  icra olunan kod deməkdir. Mənbə isə oxuna bilər və teq yoxlanıla bilər.
- Geri qaytarma: `libector.dll` + `share\extensionector*` silmək.
- Ölçülmüş nəticə: eyni 7 sorğu, eyni maşın — yalnız açar söz **6/7**, hibrid **7/7**.
  Fərqi yaradan hal: iki task `rel 0.091`-də bərabərdir və tie-break tarixə düşür; vektor tərəfi
  onları 59% vs 57% ilə ayırır. Yəni embedding modelinin əsl faydası "açar söz işləmir" deyil,
  **"iki task eyni sözlərlə fərqli problemi təsvir edəndə açar söz düzləşir"**.

## ADR-016 — Nisbi vaxt üçün Intl.RelativeTimeFormat istifadə edilmir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: UI-ı brauzerdə işlədəndə tile kartlarda "Due in in 7 hours" göründü.
- Birinci baq: `Intl.RelativeTimeFormat` onsuz da tam ifadə qaytarır ("in 7 hours"), şablon isə üstünə
  bir daha "Due in" əlavə edirdi.
- **İkinci və daha ciddi baq:** Chromium-da `az` üçün relative-time datası yoxdur —
  `new Intl.RelativeTimeFormat("az").format(7,"hour")` **"+7 h"** qaytarır. Yəni azərbaycanca UI
  "+7 h qalıb" göstərirdi. Bu, yalnız brauzerdə, yalnız az dilində görünür.
- Qərar: `formatDuration()` yalnız **kəmiyyət** qaytarır ("7 saat", "3 days") — nə ön söz, nə istiqamət.
  İstiqaməti hər dilin öz sətri verir: en `"Due in {time}"` / `"{time} ago"`,
  az `"{time} qalıb"` / `"{time} əvvəl"` / `"{time} gecikib"`.
- Səbəb: Azərbaycan dilində istiqamət kəmiyyətdən **sonra** gəlir, ona görə hazır ifadə qaytaran
  API-ni hər iki dildə düzgün cümləyə yerləşdirmək mümkün deyil. Azərbaycanca rəqəmdən sonra isim
  cəmlənmir ("7 saat"), ingiliscə isə cəmlənir — bu da lokala aid qərardır.
- Əlavə düzəliş: həll olunmuş/bağlanmış taskda SLA geri sayımı ümumiyyətlə göstərilmir — bitmiş işin
  son tarixi məna daşımır (`isSettled`).

## ADR-017 — Marşrutlaşdırma nəticəsi saxlanılır, yenidən işlətmək isə açıq əməliyyatdır
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: `GET /tickets/17/routing` 13 s, sonra 6.7 s çəkirdi.
- Səbəb: `RoutingService` `SuggestForTicketAsync`-i **draft ilə** çağırırdı — yəni hər sorğuda model
  heç kimin oxumadığı müştəri cavabını yazırdı. Eyni səhv `AssessmentService` və
  `ResolutionReviewService`-də də vardı.
- Qərar 1: `draftAnswer: false` — üç yerdə. Yalnız uyğunluqlar lazımdır.
- Qərar 2: Nəticə `ticket_routing` cədvəlində saxlanılır (namizədlər `jsonb`, çünki bütöv oxunur və
  yazılır). `GET` saxlanılanı qaytarır, ilk baxışda bir dəfə hesablayır; `POST` yenidən işlədir.
- Ölçmə: **13 s → ilk hesablama 3.0 s → sonrakı oxumalar 2 ms.**
- UI: kartda "Yenidən" düyməsi və "N əvvəl təhlil edilib" — saxlanılan nəticənin köhnəldiyi görünsün.
- Qeyd: Dapper.AOT anonim obyektdəki inline `JsonSerializer.Serialize(...)` çağırışının tipini
  çıxara bilmir və `default()` yayır. Dəyər əvvəlcə `string` lokala yazılmalıdır.

## ADR-018 — Giriş üsulu tək seçim deyil, paralel işləyən dəst
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: `Auth:Provider` **bir** üsul seçirdi. Real komandada isə eyni anda kadr üçün SSO, podratçı
  üçün parol, kimsə üçün passkey lazım ola bilər.
- Qərar: `Auth:Methods` — `[Password, Oidc, Passkey]` bayraq dəsti. `Provider` isə yalnız **parolun
  harada yoxlandığını** bildirir (Local | Ldap). Köhnə konfiqurasiya avtomatik tərcümə olunur, ona görə
  mövcud quraşdırma toxunulmadan işləməyə davam edir.
- Hesab yaradılması: **öz-özünə qeydiyyat yoxdur**. Hesabı olan biri yaradır, yeni işçiyə isə parol
  deyil, **birdəfəlik keçid** gedir. Token-in yalnız SHA-256 hash-i saxlanılır — baza sızsa belə işlək
  keçid çıxmır, və məhsul keçidi ikinci dəfə göstərə bilmir, çünki artıq bilmir. Açılan keçid yanır.
- 2FA: TOTP (RFC 6238) əl ilə yazıldı — alqoritm 30 sətirdir, reflection gətirən paket isə API-nin
  təmiz AOT build-inə baha başa gələrdi. Qeydiyyat iki addımlıdır: sirr saxlanılır, amma faktor kod
  təsdiqlənənə qədər **aktivləşmir** — əks halda səhv skan istifadəçini öz hesabından kilidləyər.
- Passkey: WebAuthn, ES256 və RS256. **Attestation yoxlanılmır** — o, "hansı marka autentifikatordur"
  sualına cavab verir; dəstək masası üçün dəyər vermir, əvəzində metadata servisi və sertifikat
  zənciri tələb edir. Təhlükəsizliyi daşıyan hissələr (challenge, origin, RP ID, imza) yoxlanılır.
- Bilinən məhdudiyyət: challenge və MFA handle-ları **yaddaşdadır**. Tək nüsxə üçün düzgündür,
  sticky session olmayan yük balanslayıcısı arxasında isə yox. Bu, sonradan kəşf edilməsin deyə
  koddə və burada açıq yazılıb.

## ADR-019 — İnteraktiv OIDC axını server tərəfdə; API öz tokenini verir
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: OIDC yalnız "provayderin tokenini yoxla" rejimində idi; SPA-dakı SSO düyməsi mövcud
  olmayan `/auth/oidc/start`-a işarə edirdi.
- Qərar: Authorization code + **PKCE (S256)** server tərəfdə icra olunur, ID token provayderin JWKS-i
  ilə yoxlanılır, sonra **ResolveDesk öz tokenini** verir.
- Səbəb: Öz tokenimizi verməsək, SSO parol və passkey ilə yan-yana dura bilməzdi — hər üsul fərqli
  format qaytarardı, rollar və müddət isə iki cür işləyərdi. ADR-006-nın "hamısı eyni JWT verir"
  prinsipi məhz budur.
- İki forma saxlanılır və bir-birini istisna edir: `ClientId` varsa interaktiv, yoxsa köhnə
  passthrough. Hansının işlədiyi **startup-un ilk loq sətrində** yazılır, çünki səhv konfiqurasiya
  başqa cür yalnız girişə cəhd edəndə üzə çıxardı.
- Token ötürülməsi: son yönləndirmədə **60 saniyəlik birdəfəlik kod**, token yox. Query string proxy
  loqlarına, fragment isə brauzer tarixçəsinə düşür; bir dəqiqəlik birdəfəlik kod hər iki yerdə
  8 saatlıq sessiyadan qat-qat az dəyərlidir.
- PKCE client secret olsa belə istifadə olunur: secret hansı klientin kodu dəyişdirdiyini sübut edir,
  PKCE isə **axını başlayan tərəfin eyni tərəf olduğunu**.
- Yoxlama: `tools/fake-oidc-provider.mjs` — real RSA açarları, imzalanmış tokenlər, PKCE **məcburi**.
  Onsuz JWKS, RS256 imzası, nonce və PKCE — yəni səhv olanda **səssizcə** sınan hissələr —
  ümumiyyətlə sınanmamış qalardı. `tools/oidc-smoke.mjs` → 25/25.
- Brauzerdə tapılan baq: `OidcCallback` sorğu sətrini effekt daxilində oxuyurdu. StrictMode effekti
  iki dəfə işlədir; birinci işləmə kodu ünvan sətrindən silir, ikincisi boş URL görüb "keçid natamam"
  deyirdi — uğurlu girişin üstündən. Sorğu modul yüklənəndə tutulur, mübadilə isə `useRef` ilə bir
  dəfə işə düşür. Skript testi bunu tuta bilməzdi.

## ADR-020 — Giriş vəziyyəti PostgreSQL-də paylaşılır, yaddaşda yox
- Tarix: 2026-09-06 · Status: Qəbul edildi
- Kontekst: MFA handle-ları, passkey challenge-ləri, OIDC state-ləri və SSO ötürmə kodları prosesin
  yaddaşında idi. Dördü də **iki sorğu arasında** yaşayır və balanslayıcı arxasında ikinci sorğu
  başqa nüsxəyə düşür. Tək nüsxədə hər şey işləyirdi; iki nüsxədə təxminən yarısı sınardı və bu,
  "istifadəçilər təsadüfən çıxış edir" kimi görünərdi.
- Qərar: Vahid `IHandleStore` → `auth_handles` cədvəli. Redis daha adi seçimdir, amma 60 saniyə yaşayan
  bir neçə sətir üçün ayrıca işlədilməli, təhlükəsizləşdirilməli və ehtiyat nüsxəsi alınmalı ikinci
  infrastruktur deməkdir. PostgreSQL onsuz da məcburidir və tranzaksiyalıdır.
- İki xassə vacibdir:
  - **Atomar istifadə:** `DELETE … RETURNING` tək ifadədir, ona görə eyni handle üzərində yarışan iki
    nüsxədən yalnız biri payload alır. Oxu-sonra-sil hər ikisini uduzduraraq keçirərdi.
  - **Sükunətdə yararsız:** yalnız SHA-256 hash saxlanılır. Bunlar bearer sirlərdir — baza dump-ından
    oxunan canlı MFA handle-ı əks halda parolun yanından keçmək yolu olardı.
- `purpose` sorğunun bir hissəsidir, ona görə passkey challenge-i MFA handle-ı kimi istifadə edilə bilməz.
- Yoxlama: `tools/multi-instance-smoke.mjs` — iki nüsxə, hər addım qəsdən nüsxə keçir (A-da başla,
  B-də bitir). **15/15**. Bu, tək nüsxəli testin göstərə bilməyəcəyi yeganə şeydir.
- **Açıq qalan:** sürət limiti hələ də proses daxilindədir, yəni N nüsxə N dəfə çox cəhdə icazə verir.
  Sorğu başına yazma tələb etdiyi üçün bazaya köçürmək düzgün deyil; adi həll balanslayıcı/reverse
  proxy səviyyəsində limitləmək, yaxud Redis-dir. Gizlətmək əvəzinə burada yazılıb.
