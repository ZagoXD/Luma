# Luma API e Docker Local

Este diretório contém a API da Luma, os testes de backend e o Docker Compose usado no desenvolvimento local.

Status atual: a API cobre WhatsApp, cadastro, ciclo menstrual, gravidez, calendário, assinaturas, Stripe, Redis, áudio por ElevenLabs, imagens educativas por OpenAI + R2, notificações estruturais e criptografia de dados reais da usuária.

## Serviços Locais

O `docker-compose.yml` sobe:

- `postgres`: banco PostgreSQL.
- `redis`: cache para rate limit, deduplicação e locks.
- `api`: API ASP.NET Core.
- `web`: aplicação Next.js.

## Subir Localmente

```powershell
Copy-Item .env.example .env
docker compose up -d --build
```

URLs:

- API: `http://localhost:5050`
- Web: `http://localhost:3000`
- Postgres: `localhost:5433`
- Redis: `localhost:6379`

Health check:

```powershell
curl.exe http://localhost:5050/health
```

## Variáveis Principais

Arquivo local:

```txt
whatsapp-app/.env
```

Principais chaves:

```env
POSTGRES_DB=luma
POSTGRES_USER=luma
POSTGRES_PASSWORD=luma_dev_password
POSTGRES_HOST_PORT=5433
REDIS_CONNECTION_STRING=redis:6379
API_HOST_PORT=5050
WEB_HOST_PORT=3000
LUMA_REQUIRE_ACTIVE_SUBSCRIPTION=true
OPENAI_API_KEY=
OPENAI_IMAGE_MODEL=gpt-image-1
ELEVENLABS_API_KEY=
PRIVACY_ENCRYPTION_ENABLED=true
PRIVACY_ENCRYPTION_KEY=
PRIVACY_LOOKUP_PEPPER=
PRIVACY_ACTIVE_KEY_ID=local-dev
STRIPE_SECRET_KEY=
STRIPE_BASIC_PRICE_ID=
STRIPE_ESSENTIAL_PRICE_ID=
STRIPE_WEBHOOK_SECRET=
TWILIO_ACCOUNT_SID=
TWILIO_AUTH_TOKEN=
TWILIO_WHATSAPP_FROM=whatsapp:+16204008668
R2_ACCOUNT_ID=
R2_BUCKET_NAME=
R2_ACCESS_KEY_ID=
R2_SECRET_ACCESS_KEY=
R2_ENDPOINT=
R2_PUBLIC_BASE_URL=
NOTIFICATIONS_WORKER_ENABLED=false
```

## Testes

```powershell
dotnet test Luma.sln
```

Status atual: 123 testes de backend passando.

## Privacidade

Dados reais sensíveis são criptografados em repouso com AES-GCM. Buscas por e-mail, CPF e telefone usam hashes HMAC, sem consultar o valor real em claro.

Campos protegidos incluem e-mail, CPF, nome, telefone, nome de exibição no WhatsApp, telefone de assinatura, metadados de eventos, payloads pendentes, corpo de mensagens quando salvo, método contraceptivo e conversas bloqueadas.

Datas operacionais continuam em colunas próprias para preservar calendário, notificações e cálculos.

## Webhook Twilio

Endpoint:

```txt
POST /webhooks/twilio/whatsapp
```

Para testar localmente, exponha a API:

```powershell
ngrok http 5050
```

Configure no Twilio:

```txt
https://SEU-TUNEL/webhooks/twilio/whatsapp
```

Método:

```txt
POST
```

## Endpoint de Teste Sem Twilio

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5050/dev/messages `
  -ContentType 'application/json; charset=utf-8' `
  -Body (@{ from = '+5516992330309'; body = 'oi' } | ConvertTo-Json -Compress)
```

## Notificações

O worker está implementado, mas deve ficar desligado até os templates Twilio/Meta estarem aprovados:

```env
NOTIFICATIONS_WORKER_ENABLED=false
```

Teste manual do processador:

```powershell
Invoke-RestMethod -Method Post http://localhost:5050/dev/notifications/run
```

Sem templates configurados, envios reais não devem ser esperados.

## Recursos por Plano

Plano Básico:

- conversa por texto;
- registros menstruais, sintomas, humor e relação sexual;
- histórico, calendário e previsões.

Plano Essencial:

- tudo do Básico;
- áudio no WhatsApp;
- notificações automáticas;
- imagens educativas do bebê e recursos visuais.

Áudio, imagens e notificações são bloqueados pelo backend para usuárias do plano Básico, com resposta orientando upgrade no painel.

## Comandos Úteis

Ver usuárias:

```powershell
curl.exe http://localhost:5050/admin/users
```

Parar containers:

```powershell
docker compose down
```

Apagar volumes locais:

```powershell
docker compose down -v
```
