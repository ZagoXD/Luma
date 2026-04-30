# Luma - Plano de fechamento operacional da V1

Este documento descreve os três fluxos finais da V1 operacional da Luma:

1. Rate limit e anti-spam com Redis.
2. Bloqueio defensivo de conversas em grupo.
3. Notificações automáticas do plano Essencial.

Status em 30/04/2026: a estrutura de backend foi implementada. Para notificações reais fora da janela de 24h ainda falta cadastrar e aprovar os templates no Twilio/Meta e configurar os SIDs nas variáveis de ambiente.

---

## Item 1 - Rate Limit e Anti-Spam com Redis

### Objetivo

Evitar custo excessivo de OpenAI, loops de webhook, mensagens duplicadas do Twilio, gravações concorrentes e respostas fora de ordem.

### Implementado

- Redis no Docker Compose.
- Pacote `StackExchange.Redis` na API.
- `RedisConnectionProvider` com fallback em memória quando Redis não estiver configurado ou estiver indisponível.
- `MessageIngressGuard` antes da conversa com a IA.
- Deduplicação por `ProviderMessageId`.
- Rate limit por telefone.
- Cooldown temporário.
- Lock curto por telefone para evitar duas mensagens simultâneas.
- Resposta acolhedora quando a usuária envia muitas mensagens em sequência.
- Retorno silencioso quando a mensagem é retry/duplicada.

### Variáveis

```env
REDIS_CONNECTION_STRING=redis:6379
LUMA_RATE_LIMIT_WINDOW_SECONDS=30
LUMA_RATE_LIMIT_MAX_MESSAGES=5
LUMA_RATE_LIMIT_COOLDOWN_SECONDS=60
LUMA_MESSAGE_LOCK_SECONDS=25
LUMA_DEDUPLICATION_SECONDS=300
```

Para Redis Cloud/Render, usar o formato:

```env
REDIS_CONNECTION_STRING=redis-11281.crce219.us-east-1-4.ec2.cloud.redislabs.com:11281,user=default,password=SUA_SENHA
```

### Fluxo

```txt
Webhook Twilio
  -> valida payload
  -> detecta grupo
  -> normaliza telefone
  -> deduplica ProviderMessageId
  -> aplica rate limit/cooldown
  -> cria lock por telefone
  -> processa ConversationService
  -> libera lock
```

### Comportamento esperado

- Se a usuária enviar até 5 mensagens em 30 segundos, o fluxo segue normalmente.
- Se passar do limite, a Luma não chama OpenAI e responde uma vez:

```txt
Recebi muitas mensagens em sequência. Vou pausar por alguns segundos para organizar tudo com segurança. Pode me chamar novamente em instantes.
```

- Se o Twilio reenviar o mesmo `MessageSid`, a API responde `200` sem processar novamente.
- Se uma mensagem chegar enquanto outra do mesmo número ainda está sendo processada, a Luma segura o fluxo e evita concorrência.

---

## Item 2 - Bloqueio Defensivo de Grupos

### Objetivo

Garantir que a Luma só converse em atendimentos individuais. Isso protege privacidade, LGPD e dados de saúde.

### Implementado

- `ConversationScopeDetector`.
- Bloqueio antes de rate limit e antes da IA.
- Registro técnico em `blocked_conversations`.
- O corpo da mensagem não é salvo quando a conversa parece ser grupo.

### Heurísticas atuais

A mensagem é bloqueada se:

- `From` contém `@g.us`;
- payload contém campos como `GroupSid`, `GroupId`, `GroupName`, `ChannelSid`, `ConversationSid` ou `ParticipantSid`;
- `From` não começa com `whatsapp:+`;
- o número normalizado não parece um telefone válido.

### Resposta segura

```txt
Oi, eu sou a Luma. Por privacidade, eu só consigo conversar em atendimentos individuais. Se quiser continuar, me chame no privado.
```

### Limitação Twilio

No WhatsApp Business oficial e no Sandbox, o Twilio geralmente trabalha com conversa 1:1. Pode não existir evento real de "adicionado em grupo" nem API para "sair do grupo". Por isso, o backend implementa proteção defensiva: se qualquer payload suspeito chegar, a Luma não processa.

---

## Item 3 - Notificações Automáticas do Plano Essencial

### Objetivo

Permitir lembretes automáticos para usuárias do plano Essencial:

- menstruação prevista para amanhã;
- menstruação prevista para hoje;
- anticoncepcional diário, quando o método for pílula;
- check-in de sintomas/humor como base para expansão.

### Implementado

- Models:
  - `NotificationPreference`
  - `NotificationDelivery`
  - `BlockedConversation`
- Tabelas runtime:
  - `notification_preferences`
  - `notification_deliveries`
  - `blocked_conversations`
- Endpoints autenticados:
  - `GET /account/notifications/preferences`
  - `POST /account/notifications/preferences`
- Endpoint dev:
  - `POST /dev/notifications/run`
- `NotificationPreferenceService`.
- `NotificationProcessor`.
- `NotificationWorker` em `BackgroundService`.
- `TwilioWhatsAppNotificationSender` usando templates Twilio (`ContentSid`).
- Redis lock para evitar envio duplicado:

```txt
luma:notification-lock:{userId}:{type}:{date}
```

### Regras implementadas

- Notificações só são enviadas para plano Essencial ativo.
- Plano Básico não recebe notificações automáticas.
- Assinatura expirada não recebe.
- Usuária com gravidez ativa não recebe previsão menstrual.
- Menstruação prevista é calculada por:

```txt
last_period_start_date + average_cycle_length
```

- Anticoncepcional diário só é enviado quando `ContraceptiveType == "pill"`.
- O índice único impede envio duplicado por tipo/dia:

```txt
unique(user_id, type, scheduled_for_date)
```

### Variáveis Twilio

```env
TWILIO_ACCOUNT_SID=
TWILIO_AUTH_TOKEN=
TWILIO_WHATSAPP_FROM=whatsapp:+14155238886
TWILIO_TEMPLATE_PERIOD_TOMORROW=
TWILIO_TEMPLATE_PERIOD_TODAY=
TWILIO_TEMPLATE_CONTRACEPTIVE_DAILY=
TWILIO_TEMPLATE_SYMPTOM_CHECKIN=
NOTIFICATIONS_WORKER_ENABLED=false
NOTIFICATIONS_WORKER_INTERVAL_SECONDS=60
```

Em produção, depois dos templates aprovados, habilitar:

```env
NOTIFICATIONS_WORKER_ENABLED=true
```

### Templates Twilio sugeridos

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

### O que ainda é manual

1. Criar os templates no Twilio/Meta.
2. Aguardar aprovação.
3. Copiar os SIDs dos templates para as variáveis `TWILIO_TEMPLATE_*`.
4. Configurar `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` e `TWILIO_WHATSAPP_FROM`.
5. Ativar `NOTIFICATIONS_WORKER_ENABLED=true` no ambiente de produção.

### Como testar localmente

1. Subir os serviços:

```powershell
cd whatsapp-app
docker compose up --build
```

2. Criar conta/plano Essencial pela web.
3. Conversar com a Luma pelo WhatsApp para criar o perfil menstrual.
4. Salvar preferências pelo endpoint autenticado ou pela futura tela web:

```http
POST /account/notifications/preferences
Authorization: Bearer TOKEN

{
  "periodReminderEnabled": true,
  "contraceptiveReminderEnabled": true,
  "symptomCheckinEnabled": false,
  "reminderTime": "21:00",
  "timeZone": "America/Sao_Paulo"
}
```

5. Rodar o worker manualmente:

```powershell
Invoke-RestMethod -Method Post http://localhost:5050/dev/notifications/run
```

Sem templates configurados, o delivery será criado e marcado como `failed` com erro `twilio_template_not_configured`. Isso é esperado até os templates serem cadastrados.

---

## Testes Implementados

- Detector libera conversa 1:1.
- Detector bloqueia sinais de grupo.
- Parser de horário aceita formatos naturais como `8`, `08:30`, `20h` e `às 21h15`.
- Parser rejeita horários inválidos.
- Rate limit bloqueia mensagens acima do limite configurado.
- Deduplicação ignora o mesmo `ProviderMessageId`.

Comando:

```powershell
dotnet test whatsapp-app/Luma.sln
```

---

## Definição de Pronto

Esta etapa fica pronta quando:

- Redis sobe pelo Docker Compose.
- API compila sem warnings.
- Testes passam.
- Rate limit não chama OpenAI durante cooldown.
- Payloads de grupo não chegam ao motor de IA.
- Preferências de notificação podem ser salvas.
- Worker calcula notificações.
- Deliveries são registrados.
- Fica faltando apenas template aprovado/configurado no Twilio para envio real fora da janela de 24h.
