# Luma

A Luma é uma assistente de WhatsApp para acompanhamento de ciclo menstrual e gravidez. Ela ajuda usuárias a registrar menstruação, sintomas, humor, relação sexual, dados de gravidez, calendário e lembretes, com conversa acolhedora e limites claros de segurança.

Ela não faz diagnósticos, não confirma gravidez, não substitui orientação médica e orienta buscar um profissional de saúde quando o assunto envolve risco.

## Estado Atual

Implementado:

- Bot WhatsApp via Twilio.
- API em ASP.NET Core 8.
- Web em Next.js.
- PostgreSQL.
- Redis.
- OpenAI como motor de IA.
- Stripe Billing com Stripe Elements.
- Cadastro web com autenticação JWT/cookie.
- Perfil da usuária.
- Validação de assinatura antes de responder no WhatsApp.
- Cadastro inicial pelo WhatsApp.
- Fluxo menstrual completo.
- Registro de relação sexual.
- Fluxo de gravidez.
- Respostas sobre desenvolvimento do bebê por semana.
- Geração opcional de imagem educativa do bebê com OpenAI Images + Cloudflare R2.
- Calendário visual mensal na web.
- Pedido de calendário pelo WhatsApp com link direto para o mês solicitado.
- Guardrails médicos.
- Rate limit e anti-spam.
- Bloqueio defensivo de grupos.
- Estrutura de notificações do plano Essencial.

Pendente para produção completa:

- Criar/aprovar templates Twilio/Meta.
- Ativar worker de notificações.
- Configurar WhatsApp Business real fora do Sandbox.
- Configurar Stripe em modo produção.
- Configurar domínio customizado para mídia do R2.
- Publicar termos e política de privacidade.

## Arquitetura

```txt
Usuária no WhatsApp
  -> Twilio
  -> API Luma
  -> Redis para rate limit, dedupe e locks
  -> PostgreSQL para dados autoritativos
  -> OpenAI para interpretação, resposta e imagem opcional
  -> Cloudflare R2 para mídia temporária
  -> Twilio responde no WhatsApp
```

Web:

```txt
Next.js
  -> API Luma
  -> Stripe Elements
  -> Stripe Billing
  -> PostgreSQL
```

## Estrutura

```txt
Luma/
  specs/
    documentação de produto, stack, roadmap e integrações
  whatsapp-app/
    API ASP.NET Core, testes, Docker Compose, Postgres e Redis locais
  web/
    aplicação Next.js
```

## Subir Localmente

1. Copie os arquivos de ambiente:

```powershell
Copy-Item whatsapp-app/.env.example whatsapp-app/.env
Copy-Item web/.env.example web/.env
```

2. Configure pelo menos:

```env
OPENAI_API_KEY=
NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY=
STRIPE_SECRET_KEY=
STRIPE_BASIC_PRICE_ID=
STRIPE_ESSENTIAL_PRICE_ID=
STRIPE_WEBHOOK_SECRET=
```

3. Para geração de imagens do bebê, configure também:

```env
OPENAI_IMAGE_MODEL=gpt-image-1
R2_ACCESS_KEY_ID=
R2_SECRET_ACCESS_KEY=
R2_PUBLIC_BASE_URL=https://pub-7621f98d02d741da84d6fd1b054da6d5.r2.dev
```

4. Suba os serviços:

```powershell
cd whatsapp-app
docker compose up -d --build
```

URLs locais:

- Web: `http://localhost:3000`
- API: `http://localhost:5050`
- Health: `http://localhost:5050/health`
- Postgres: `localhost:5433`
- Redis: `localhost:6379`

## Testes

Backend:

```powershell
dotnet test whatsapp-app/Luma.sln
```

Web:

```powershell
cd web
npm run lint
npm run build
```

Docker:

```powershell
cd whatsapp-app
docker compose --env-file .env -f docker-compose.yml config --quiet
docker compose --env-file .env -f docker-compose.yml build api web
```

## WhatsApp com Twilio

Webhook:

```txt
POST /webhooks/twilio/whatsapp
```

Para testar localmente, exponha a API com ngrok ou equivalente:

```powershell
ngrok http 5050
```

Configure no Twilio Sandbox:

```txt
https://SEU-TUNEL/webhooks/twilio/whatsapp
```

Método:

```txt
POST
```

## Stripe

O checkout usa Stripe Elements.

Cartão de teste:

```txt
4242 4242 4242 4242
```

Mais detalhes:

```txt
specs/tutorial-stripe-luma.md
```

## Redis

Local:

```env
REDIS_CONNECTION_STRING=redis:6379
```

Render/API:

```env
Redis__ConnectionString=HOST:PORT,user=default,password=SENHA
```

## R2

O bucket atual é `luma`, com prefixo `baby-image-generation/`.

Use uma Lifecycle Rule no R2 para remover objetos desse prefixo depois de 1 dia. Não apague instantaneamente, porque Twilio/Meta pode buscar a mídia com atraso ou retry.

## Notificações

O worker de notificações existe, mas deve ficar desligado até os templates Twilio serem aprovados:

```env
Notifications__WorkerEnabled=false
```

Depois dos templates:

```env
Notifications__WorkerEnabled=true
```

## Documentação

Principais documentos:

- `specs/especificacao-bot-ciclo-menstrual-whatsapp.md`
- `specs/especificacao-stacks-luma.md`
- `specs/roadmap-proximas-etapas-luma.md`
- `specs/plano-fechamento-v1-operacional.md`
- `specs/plano-final-v1-gravidez-bebe-calendario.md`
- `specs/tutorial-stripe-luma.md`
- `specs/proposta-migracao-openai-luma.md`
