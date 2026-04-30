# Luma API e Docker Local

Este diretório contém a API da Luma, os testes de backend e o Docker Compose usado no desenvolvimento local.

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
STRIPE_SECRET_KEY=
STRIPE_BASIC_PRICE_ID=
STRIPE_ESSENTIAL_PRICE_ID=
STRIPE_WEBHOOK_SECRET=
NOTIFICATIONS_WORKER_ENABLED=false
```

## Testes

```powershell
dotnet test Luma.sln
```

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
