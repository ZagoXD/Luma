# Luma

A Luma é uma assistente de WhatsApp para acompanhamento de ciclo menstrual e gravidez. Ela ajuda usuárias a registrar menstruação, sintomas, humor, relação sexual, dados de gravidez, calendário e lembretes, com conversa acolhedora, validações de assinatura e limites claros de segurança.

Ela não faz diagnósticos, não confirma gravidez, não substitui orientação médica e orienta buscar um profissional de saúde quando o assunto envolve risco.

## Estado Atual

Implementado:

- Bot WhatsApp via Twilio.
- API em ASP.NET Core 8.
- Web em Next.js.
- PostgreSQL.
- Redis para rate limit, deduplicação, cooldown e locks.
- OpenAI como motor de IA, tools e respostas humanizadas.
- ElevenLabs para transcrição de áudio no WhatsApp.
- Stripe Billing com Stripe Elements.
- Cadastro web com autenticação JWT/cookie.
- Checkbox de consentimento LGPD no cadastro web.
- Perfil da usuária.
- Validação de assinatura antes de responder no WhatsApp.
- Planos Básico e Essencial.
- Bloqueio de recursos premium para plano Básico.
- Cadastro inicial pelo WhatsApp.
- Fluxo menstrual completo.
- Registro de relação sexual.
- Fluxo de gravidez.
- Respostas sobre desenvolvimento do bebê por semana.
- Geração opcional de imagem educativa do bebê com OpenAI Images + Cloudflare R2.
- Calendário visual mensal na web.
- Pedido de calendário pelo WhatsApp com link direto para o mês solicitado.
- Guardrails médicos.
- Bloqueio defensivo de grupos.
- Estrutura de notificações do plano Essencial.
- Criptografia de dados reais da usuária em repouso, com AES-GCM e hashes HMAC para busca.

Pendente para produção completa:

- Criar/aprovar templates Twilio/Meta.
- Ativar worker de notificações.
- Configurar WhatsApp Business real fora do Sandbox.
- Configurar Stripe em modo produção.
- Configurar domínio customizado para mídia do R2.
- Publicar termos e política de privacidade.
- Definir rotina formal de backup e rotação futura de chaves de privacidade.

## Planos

### Básico

- Libera conversa por texto no WhatsApp.
- Registro de menstruação, sintomas, humor e relação sexual.
- Histórico e calendário visual do ciclo.
- Previsões estimadas de menstruação.

### Essencial

- Tudo do Básico.
- Mensagens por áudio no WhatsApp.
- Notificações automáticas e lembretes.
- Imagens educativas do bebê e outros recursos visuais.

Quando uma usuária do Básico tenta usar áudio, imagens ou notificações, o backend bloqueia e envia uma mensagem com link para upgrade no painel.

## Arquitetura

```txt
Usuária no WhatsApp
  -> Twilio
  -> API Luma
  -> Redis para rate limit, dedupe e locks
  -> PostgreSQL para dados autoritativos
  -> OpenAI para interpretação, tools, resposta e imagem opcional
  -> ElevenLabs para transcrição de áudio, quando plano Essencial
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

## Segurança e Privacidade

A API criptografa dados reais sensíveis antes de salvar no banco:

- e-mail;
- CPF;
- nome;
- telefone;
- nome/apelido da usuária no WhatsApp;
- telefone em assinaturas;
- metadados de eventos;
- payloads de intenções pendentes;
- corpo de mensagens, caso o armazenamento seja habilitado;
- método contraceptivo;
- dados de conversas bloqueadas.

Busca por e-mail, CPF e telefone é feita por hashes HMAC (`EmailHash`, `CpfHash`, `PhoneHash`), sem consultar o valor real em claro.

Datas operacionais continuam em colunas próprias para manter calendário, notificações e cálculos funcionando. A criptografia de datas exatas pode ser uma próxima etapa usando índices cegos por mês/dia.

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

3. Configure privacidade:

```env
PRIVACY_ENCRYPTION_ENABLED=true
PRIVACY_ENCRYPTION_KEY=base64_com_32_bytes
PRIVACY_LOOKUP_PEPPER=outro_base64_com_32_bytes
PRIVACY_ACTIVE_KEY_ID=local-dev
```

4. Para áudio no WhatsApp, configure:

```env
ELEVENLABS_API_KEY=
ELEVENLABS_BASE_URL=https://api.elevenlabs.io/v1
ELEVENLABS_SPEECH_TO_TEXT_MODEL=scribe_v2
ELEVENLABS_LANGUAGE_CODE=pt
```

5. Para geração de imagens do bebê, configure também:

```env
OPENAI_IMAGE_MODEL=gpt-image-1
R2_ACCESS_KEY_ID=
R2_SECRET_ACCESS_KEY=
R2_PUBLIC_BASE_URL=https://pub-7621f98d02d741da84d6fd1b054da6d5.r2.dev
```

6. Suba os serviços:

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

Status atual: 123 testes passando.

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

## Deploy no Render

API:

```env
OpenAI__ApiKey=
OpenAI__Model=gpt-5.4-mini
ElevenLabs__ApiKey=
Privacy__EncryptionEnabled=true
Privacy__EncryptionKey=
Privacy__LookupPepper=
Privacy__ActiveKeyId=prod-2026-01
Redis__ConnectionString=
ConnectionStrings__DefaultConnection=
Stripe__SecretKey=
Stripe__WebhookSecret=
Twilio__AccountSid=
Twilio__AuthToken=
Twilio__WhatsAppFrom=whatsapp:+14155238886
```

Web:

```env
LUMA_API_BASE_URL=https://sua-api
NEXT_PUBLIC_API_BASE_URL=https://sua-api
NEXT_PUBLIC_BASE_URL=https://sua-web
NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY=
NEXT_PUBLIC_LUMA_WHATSAPP_NUMBER=+14155238886
LUMA_COOKIE_SECURE=true
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
- `specs/plano-criptografia-dados-reais-usuarios-luma.md`
