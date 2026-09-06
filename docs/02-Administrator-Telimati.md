# ResolveDesk — Administrator Təlimatı

**Layihə:** ResolveDesk
**Sonuncu yeniləmə:** 2026-09-06

---

## 1. Konfiqurasiya

Bütün açarlar `appsettings.json`-dadır və mühit dəyişəni ilə əvəzlənə bilər (`Ai__Chat__Provider`
formasında). Tam siyahı: `.env.example`. **Heç bir sirr fayla yazılmır.**

### AI — chat və embedding ayrıdır

```jsonc
"Ai": {
  "Enabled": true,
  "Chat":      { "Provider": "Ollama", "BaseUrl": "http://localhost:11434", "Model": "qwen2.5-coder:7b" },
  "Embedding": { "Provider": "Ollama", "BaseUrl": "http://localhost:11434", "Model": "nomic-embed-text", "Dimensions": 768 }
}
```

`Provider`: `None | Ollama | OpenAi | AzureOpenAi | Gemini`.
`OpenAi` bütün OpenAI-uyğun serverləri əhatə edir — LM Studio, vLLM, llama.cpp, OpenRouter: sadəcə
`BaseUrl`-i onlara yönəldin. Chat hosted, embedding lokal (və ya əksi) ola bilər.

> `Dimensions` modelin verdiyi ölçü ilə **eyni olmalıdır** — pgvector sütunu sabit enlidir. Uyğunsuzluq
> aydın xəta verir. Dəyişdirmək = miqrasiya + yenidən embed.

### Autentifikasiya

`Auth:Provider` — `Local` | `Ldap` | `Oidc`. Yalnız credential yoxlaması dəyişir; token, claim-lər və
icazə siyasətləri eynidir.

```jsonc
"Auth": {
  "Enabled": true,
  "Provider": "Ldap",
  "Ldap": {
    "Host": "dc01.example.org", "Port": 636, "UseSsl": true,
    "BaseDn": "DC=example,DC=org",
    "BindDnTemplate": "{0}@example.org",
    "CoordinatorGroup": "CN=ResolveDesk-Coordinators",
    "AdminGroup": "CN=ResolveDesk-Admins",
    "AutoProvisionUsers": true
  }
}
```

- **Local** — parollar `users` cədvəlində PBKDF2-SHA256 (210 000 iterasiya) ilə saxlanılır.
- **Ldap** — uğurlu bind özü sübutdur; parol burada **saxlanılmır**. Qrup üzvlüyü rola çevrilir.
- **Oidc** — axını SPA icra edir, API yalnız tokeni yoxlayır.

`Auth__Jwt__SigningKey` ən azı 32 bayt olmalıdır və Development-dən kənarda **məcburidir**
(yoxdursa tətbiq başlamır). Yaratmaq: `openssl rand -base64 48`.

## 2. İstifadəçi və icazə idarəetməsi

**Default hesab yoxdur.** İlk administrator bir dəfəlik yaradılır:

```
Auth__BootstrapAdminEmail=admin@example.org
Auth__BootstrapAdminPassword=<güclü parol>
```

Tətbiq başlayanda hesab yaradılır və xəbərdarlıq loglanır. **İlk girişdən sonra parolu dəyişin və hər
iki dəyişəni mühitdən silin.**

Rollar: `Agent` (task görür), `Coordinator` (task yönləndirir), `Admin` (istifadəçi yaradır).

## 3. Xarici servis / inteqrasiya

| Servis | Məqsəd | Olmasa nə olur |
|---|---|---|
| PostgreSQL | Əsas baza | Tətbiq işləmir |
| pgvector | Semantik axtarış | Açar söz axtarışına keçir |
| Chat modeli | Cavab layihəsi | Uyğunluqlar cavabsız qaytarılır |
| Embedding modeli | Vektor axtarış | Açar söz axtarışına keçir |
| LDAP / OIDC | Autentifikasiya | Local provayder işlədilir |

## 4. Monitorinq və diaqnostika

- `GET /health/live` — proses sağdır
- `GET /health/ready` — trafik qəbul etməyə hazırdır
- `GET /api/v1/ai/status` — **birinci baxılacaq yer**: hansı provayder, hansı model, əlçatandırmı,
  arxivin neçə faizi indekslənib. **API açarı heç vaxt göstərilmir.**
- `GET /api/v1/knowledge/stats` — indeks əhatəsi

Loglar Serilog ilə compact JSON formatındadır (stdout) — log toplayıcıya birbaşa verilə bilər.

## 5. Backup

Yedəklənməli olan yeganə şey PostgreSQL-dir. `ticket_embeddings` cədvəli **törəmə məlumatdır** —
yedəkdən kənarda qala bilər; arxa fon prosesi onu modeldən yenidən qurur.
