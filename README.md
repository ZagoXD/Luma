# Luma

A Luma é uma assistente de WhatsApp para acompanhamento de ciclo menstrual e gravidez. Ela ajuda usuárias a registrar menstruação, sintomas, humor, relação sexual, dados de gravidez e lembretes, com uma conversa acolhedora e limites claros de segurança.

Ela não faz diagnósticos, não confirma gravidez, não substitui orientação médica e orienta buscar um profissional de saúde quando o assunto envolve risco.

## Estado Atual

Implementado:

- Bot WhatsApp via Twilio.
- API em ASP.NET Core 8.
- Web em Next.js.
- PostgreSQL.
- Redis.
- OpenAI como motor de IA.
- Stripe Billing.
- Cadastro web com autenticação.
- Checkout com Stripe Elements.
- Perfil da usuária.
- Validação de assinatura antes de responder no WhatsApp.
- Cadastro inicial pelo WhatsApp.
- Fluxo menstrual completo.
- Fluxo de gravidez.
- Relação sexual registrada.
- Guardrails médicos.
- Rate limit e anti-spam.
- Bloqueio defensivo de grupos.
- Estrutura de notificações do plano Essencial.

Pendente para produção completa:

- Criar/aprovar templates Twilio/Meta.
- Ativar worker de notificações.
- Configurar WhatsApp Business real fora do Sandbox.
- Configurar Stripe em modo produção.
- Publicar termos e política de privacidade.

## Arquitetura

```txt
Usuária no WhatsApp
  -> Twilio
  -> API Luma
  -> Redis para rate limit, dedupe e locks
  -> PostgreSQL para dados autoritativos
  -> OpenAI para interpretação e resposta
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

## Estrutura do Projeto

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

3. Suba os serviços:

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

Mais detalhes em:

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
- `specs/tutorial-stripe-luma.md`
- `specs/proposta-migracao-openai-luma.md`
