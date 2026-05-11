# Luma - Plano de Fechamento Operacional da V1

Última atualização: 03/05/2026.

Este documento descreve os três fluxos finais da V1 operacional da Luma:

1. Rate limit e anti-spam com Redis.
2. Bloqueio defensivo de conversas em grupo.
3. Notificações automáticas do plano Essencial.

Status atual: a estrutura de backend, web, banco, Docker, Redis, áudio, bloqueios por plano e criptografia de dados reais foi implementada. O sender oficial da Luma está configurado na Twilio e os templates de notificação já estão elegíveis para WhatsApp business initiated e WhatsApp user initiated.

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
Twilio__WhatsAppFrom=whatsapp:+16204008668
Twilio__TemplatePeriodTomorrow=HX4b51b08ea4f3c17dcd443c1e3071995b
Twilio__TemplatePeriodToday=HX39b4b60a687825f5bb3665ca2fcb3907
Twilio__TemplateContraceptiveDaily=HXa23267cf19348a4fa39f958164125141
Twilio__TemplateSymptomCheckin=HX459958fb7dfe7243b0eb43064e77021e
Notifications__WorkerEnabled=false
Notifications__WorkerIntervalSeconds=60
```

Templates ativos:

Observação: as versões `ptbr_v2`/`ptbr_v3` foram criadas para corrigir encoding de acentuação nos templates anteriores. Enquanto elas estiverem pendentes de elegibilidade WhatsApp, mantenha `Notifications__WorkerEnabled=false`. Depois que aparecerem como elegíveis para WhatsApp business initiated, altere para `true`.

`luma_period_tomorrow_ptbr_v2`

```txt
Olá, {{1}}. Sua menstruação está prevista para amanhã. Se quiser, posso te ajudar a acompanhar sintomas, fluxo ou humor por aqui.
```

`luma_period_today_ptbr_v2`

```txt
Olá, {{1}}. Sua menstruação está prevista para hoje. Se ela começar, pode me responder "menstruei hoje" que eu registro para você.
```

`luma_contraceptive_daily_ptbr_v3`

```txt
Olá, {{1}}. Passando para lembrar do seu anticoncepcional de hoje, como combinado para {{2}}. Se já tomou, pode seguir tranquila.
```

`luma_symptom_checkin_ptbr_v2`

```txt
Olá, {{1}}. Como você está se sentindo hoje? Se quiser, pode me contar sobre cólica, fluxo, humor ou outros sintomas.
```

## O Que Falta Manualmente

1. Manter os SIDs acima configurados no Render/API.
2. Alterar `Notifications__WorkerEnabled=true` no ambiente em que as notificações devem rodar depois que os templates novos estiverem elegíveis.
3. Configurar as variáveis de privacidade no ambiente de produção.
4. Configurar a chave da ElevenLabs no ambiente de produção.
5. Configurar monitoramento/logs para falhas de envio da Twilio.

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
