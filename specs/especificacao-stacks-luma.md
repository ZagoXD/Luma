# Especificação de Stacks da Luma

Última atualização: 03/05/2026.

## Visão Geral

A Luma é composta por:

- API ASP.NET Core 8.
- Web Next.js.
- PostgreSQL.
- Redis.
- OpenAI API.
- ElevenLabs Speech to Text.
- Twilio WhatsApp.
- Stripe Billing.
- Cloudflare R2.
- Docker para desenvolvimento local.
- Render como ambiente atual de teste/deploy.

## Backend

Stack:

- C#.
- ASP.NET Core 8 Minimal APIs.
- Entity Framework Core.
- Npgsql/PostgreSQL.
- xUnit para testes.

Responsabilidades:

- Webhook Twilio.
- Orquestração da conversa.
- Validação autoritativa das ações sugeridas pela IA.
- Persistência de cadastro, eventos de ciclo, gravidez e assinatura.
- Guardrails fixos.
- Webhooks Stripe.
- Rate limit e locks com Redis.
- Worker de notificações.
- Transcrição de áudio.
- Geração de imagens educativas.
- Criptografia de dados reais da usuária em repouso.

## Frontend Web

Stack:

- Next.js.
- React.
- TypeScript.
- Atomic Design na organização de componentes:
  - `atoms`
  - `molecules`
  - `organisms`
  - `templates`
- Stripe Elements.

Responsabilidades:

- Landing page.
- Login e criação de conta.
- Perfil da usuária.
- Checkout.
- Gestão de assinatura.
- Troca de cartão.
- Configuração de notificações.
- Link para WhatsApp da Luma.
- Calendário visual mensal.

## Banco de Dados

Banco:

- PostgreSQL local via Docker.
- PostgreSQL gerenciado no Render para teste.

Tabelas principais:

- `users`
- `user_preferences`
- `consents`
- `cycles`
- `cycle_events`
- `pregnancies`
- `pending_intents`
- `messages`
- `account_users`
- `account_sessions`
- `account_subscriptions`
- `notification_preferences`
- `notification_deliveries`
- `blocked_conversations`

Observação:

- O projeto ainda usa criação/atualização runtime de schema pela API.
- Em produção madura, o recomendado é migrar para migrations formais do EF Core.
- Dados reais sensíveis são criptografados com AES-GCM.
- Buscas por e-mail, CPF e telefone usam hashes HMAC.
- Datas operacionais seguem em claro por enquanto para preservar cálculos, calendário e notificações.

## Redis

Uso:

- Rate limit por telefone.
- Cooldown.
- Deduplicação de webhook.
- Lock por telefone.
- Lock de notificações.

Desenvolvimento local:

```env
REDIS_CONNECTION_STRING=redis:6379
```

Render/Redis Cloud:

```env
Redis__ConnectionString=HOST:PORT,user=default,password=SENHA
```

Se TLS for exigido:

```env
Redis__ConnectionString=HOST:PORT,user=default,password=SENHA,ssl=true,abortConnect=false
```

## IA

Stack atual:

- OpenAI API.
- Modelo configurável por variável de ambiente.
- Respostas estruturadas para escolha de tools.
- Geração de resposta final acolhedora.

Variáveis:

```env
OpenAI__ApiKey=
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__Model=gpt-5.4-mini
OpenAI__TimeoutSeconds=12
OpenAI__MaxOutputTokens=700
OpenAI__ReasoningEffort=none
```

Histórico:

- Ollama foi usado como possibilidade inicial, mas foi removido do projeto.
- A Luma agora usa OpenAI em desenvolvimento e produção para manter comportamento consistente.

## Áudio

Stack atual:

- ElevenLabs Speech to Text.
- Download de mídia da Twilio usando credenciais da conta.
- Recurso disponível apenas no plano Essencial.

Variáveis:

```env
ElevenLabs__ApiKey=
ElevenLabs__BaseUrl=https://api.elevenlabs.io/v1
ElevenLabs__SpeechToTextModel=scribe_v2
ElevenLabs__LanguageCode=pt
ElevenLabs__TimeoutSeconds=30
ElevenLabs__MaxAudioBytes=10485760
```

## Privacidade

Stack atual:

- AES-GCM para criptografia de campos.
- HMAC-SHA256 para índices de busca.
- Backfill automático no startup.

Variáveis:

```env
Privacy__EncryptionEnabled=true
Privacy__EncryptionKey=
Privacy__LookupPepper=
Privacy__ActiveKeyId=prod-2026-01
```

Campos protegidos incluem e-mail, CPF, nome, telefone, nome de exibição, telefone de assinatura, metadados de eventos, payloads pendentes, corpo de mensagens quando salvo, método contraceptivo e conversas bloqueadas.

## WhatsApp

Provedor:

- Twilio WhatsApp Sandbox no desenvolvimento.
- Twilio WhatsApp Business em produção.

Webhook:

```txt
POST /webhooks/twilio/whatsapp
```

Limitações:

- Mensagens proativas fora da janela de 24h exigem templates aprovados.
- O Sandbox tem limitações para testes de templates e mensagens proativas.

## Pagamentos

Stack:

- Stripe Billing.
- Stripe Elements.
- Stripe Webhooks.

Planos:

- Básico: R$ 5,90/mês.
- Essencial: R$ 9,90/mês.

Diferenciais:

- Básico: conversa por texto, registros, histórico, calendário e previsões.
- Essencial: áudio, notificações automáticas, imagens educativas e recursos visuais.

Funcionalidades:

- criação de assinatura;
- confirmação de pagamento;
- cancelamento ao fim do período;
- retomada;
- troca de plano;
- troca de cartão;
- salvamento de cartão como método padrão da assinatura.

## Docker Local

Serviços:

- `postgres`
- `redis`
- `api`
- `web`

Comando:

```powershell
cd whatsapp-app
docker compose up -d --build
```

Portas:

- Web: `http://localhost:3000`
- API: `http://localhost:5050`
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

Status atual:

- 123 testes de backend passando.
- Build e lint da web passando.
