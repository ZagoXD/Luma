# Luma - Plano de Fechamento Operacional da V1

Última atualização: 03/05/2026.

Este documento descreve os três fluxos finais da V1 operacional da Luma:

1. Rate limit e anti-spam com Redis.
2. Bloqueio defensivo de conversas em grupo.
3. Notificações automáticas do plano Essencial.

Status atual: a estrutura de backend, web, banco, Docker, Redis, áudio, bloqueios por plano e criptografia de dados reais foi implementada. Para notificações reais fora da janela de 24h ainda falta cadastrar e aprovar os templates no Twilio/Meta e configurar os SIDs nas variáveis de ambiente.

## Item 1 - Rate Limit e Anti-Spam com Redis

Implementado:

- Redis no Docker Compose.
- `StackExchange.Redis`.
- `RedisConnectionProvider` com fallback em memória.
- `MessageIngressGuard`.
- Deduplicação por `ProviderMessageId`.
- Rate limit por telefone.
- Cooldown temporário.
- Lock curto por telefone.

Variáveis locais:

```env
REDIS_CONNECTION_STRING=redis:6379
LUMA_RATE_LIMIT_WINDOW_SECONDS=30
LUMA_RATE_LIMIT_MAX_MESSAGES=5
LUMA_RATE_LIMIT_COOLDOWN_SECONDS=60
LUMA_MESSAGE_LOCK_SECONDS=25
LUMA_DEDUPLICATION_SECONDS=300
```

Variáveis no Render/API:

```env
Redis__ConnectionString=HOST:PORT,user=default,password=SENHA
Luma__RateLimit__WindowSeconds=30
Luma__RateLimit__MaxMessages=5
Luma__RateLimit__CooldownSeconds=60
Luma__MessageLockSeconds=25
Luma__DeduplicationSeconds=300
```

Fluxo:

```txt
Webhook Twilio
  -> valida payload
  -> detecta grupo
  -> normaliza telefone
  -> deduplica MessageSid
  -> aplica rate limit/cooldown
  -> cria lock por telefone
  -> processa a conversa
  -> libera lock
```

Resposta em cooldown:

```txt
Recebi muitas mensagens em sequência. Vou pausar por alguns segundos para organizar tudo com segurança. Pode me chamar novamente em instantes.
```

## Item 2 - Bloqueio Defensivo de Grupos

Implementado:

- `ConversationScopeDetector`.
- Verificação antes da IA e antes do rate limit.
- Registro técnico em `blocked_conversations`.
- Corpo da mensagem não é salvo.

Heurísticas:

- `From` contém `@g.us`.
- Payload contém campos como `GroupSid`, `GroupId`, `GroupName`, `ChannelSid`, `ConversationSid` ou `ParticipantSid`.
- `From` não começa com `whatsapp:+`.
- Telefone normalizado parece inválido.

Resposta segura:

```txt
Oi, eu sou a Luma. Por privacidade, eu só consigo conversar em atendimentos individuais. Se quiser continuar, me chame no privado.
```

Observação:

- O Twilio WhatsApp Business normalmente opera em conversa 1:1.
- Não há garantia de evento real para "sair de grupo" no Sandbox.
- O bloqueio atual é defensivo para qualquer payload suspeito.

## Item 3 - Notificações Automáticas do Plano Essencial

Implementado:

- Models:
  - `NotificationPreference`
  - `NotificationDelivery`
  - `BlockedConversation`
- Tabelas:
  - `notification_preferences`
  - `notification_deliveries`
  - `blocked_conversations`
- Endpoints:
  - `GET /account/notifications/preferences`
  - `POST /account/notifications/preferences`
  - `POST /dev/notifications/run`
- Worker:
  - `NotificationWorker`
  - `NotificationProcessor`
- Envio:
  - `TwilioWhatsAppNotificationSender`
- Tools de IA:
  - `get_notification_preferences`
  - `update_notification_preferences`
  - `disable_notification_preferences`
- Bloqueio para plano Básico com link de upgrade no perfil.

Regras:

- Apenas plano Essencial ativo recebe notificações automáticas.
- Assinatura expirada não recebe.
- Gravidez ativa bloqueia previsão menstrual.
- Anticoncepcional diário só é enviado para pílula na V1.
- Redis lock e índice único no Postgres evitam duplicidade.

Variáveis:

```env
Twilio__AccountSid=
Twilio__AuthToken=
Twilio__WhatsAppFrom=whatsapp:+14155238886
Twilio__TemplatePeriodTomorrow=
Twilio__TemplatePeriodToday=
Twilio__TemplateContraceptiveDaily=
Twilio__TemplateSymptomCheckin=
Notifications__WorkerEnabled=false
Notifications__WorkerIntervalSeconds=60
```

Templates sugeridos:

`luma_period_tomorrow`

```txt
Olá, {{1}}. Sua menstruação está prevista para amanhã. Se quiser, posso te ajudar a acompanhar sintomas, fluxo ou humor por aqui.
```

`luma_period_today`

```txt
Olá, {{1}}. Sua menstruação está prevista para hoje. Se ela começar, pode me responder "menstruei hoje" que eu registro para você.
```

`luma_contraceptive_daily`

```txt
Olá, {{1}}. Passando para lembrar do seu anticoncepcional de hoje, como combinado para {{2}}.
```

`luma_symptom_checkin`

```txt
Olá, {{1}}. Como você está se sentindo hoje? Se quiser, pode me contar sobre cólica, fluxo, humor ou outros sintomas.
```

## O Que Falta Manualmente

1. Criar os templates no Twilio/Meta.
2. Aguardar aprovação.
3. Copiar os SIDs dos templates para as variáveis `Twilio__Template*`.
4. Configurar credenciais reais da Twilio.
5. Ativar `Notifications__WorkerEnabled=true` somente quando o envio real estiver pronto.
6. Configurar as variáveis de privacidade no ambiente de produção.
7. Configurar a chave da ElevenLabs no ambiente de produção.

## Validação Atual

Comandos validados localmente:

```powershell
dotnet build whatsapp-app/Luma.sln
dotnet test whatsapp-app/Luma.sln --no-build
cd web
npm run build
npm run lint
cd ../whatsapp-app
docker compose up -d --build
```

Status:

- API saudável.
- Redis respondendo `PONG`.
- Tabelas novas criadas no Postgres local.
- Endpoint `/dev/notifications/run` respondendo.
- Criptografia de dados reais validada no banco local.
- 123 testes de backend passando.
