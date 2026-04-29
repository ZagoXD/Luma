# Luma WhatsApp Bot MVP

Backend da Luma para cadastro e conversa pelo WhatsApp, usando:

- ASP.NET Core 8
- PostgreSQL
- Entity Framework Core
- OpenAI API
- Docker Compose
- Twilio WhatsApp Sandbox via webhook TwiML

## Subir Localmente

Copie o arquivo de exemplo de ambiente:

```powershell
Copy-Item .env.example .env
```

Configure `OPENAI_API_KEY` no `.env`. O arquivo `.env` não deve ser commitado.

```powershell
docker compose up -d --build
```

API local:

```txt
http://localhost:5050
```

Postgres local fica exposto em `localhost:5433` para não conflitar com outro banco que já use `5432`.

Health check:

```powershell
curl.exe http://localhost:5050/health
```

Rodar testes:

```powershell
dotnet test Luma.sln
```

## Variáveis De Ambiente

As variáveis ficam em `.env`, baseado em `.env.example`.

```txt
POSTGRES_DB=luma
POSTGRES_USER=luma
POSTGRES_PASSWORD=luma_dev_password
POSTGRES_HOST_PORT=5433
API_HOST_PORT=5050
ASPNETCORE_ENVIRONMENT=Development
LUMA_STORE_MESSAGE_BODIES=false
OPENAI_API_KEY=
OPENAI_MODEL=gpt-5.4-mini
OPENAI_TIMEOUT_SECONDS=12
OPENAI_MAX_OUTPUT_TOKENS=700
OPENAI_REASONING_EFFORT=none
```

No estado atual, não há segredo da Twilio no backend. A Twilio chama o webhook e a API responde com TwiML na própria requisição.

## Teste Sem Twilio

Use o endpoint local de desenvolvimento:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5050/dev/messages `
  -ContentType 'application/json; charset=utf-8' `
  -Body (@{ from = '+5516992330309'; body = 'oi' } | ConvertTo-Json -Compress)
```

## Twilio WhatsApp Sandbox

O webhook implementado é:

```txt
POST /webhooks/twilio/whatsapp
```

Twilio precisa acessar uma URL pública. Para testar localmente, exponha a porta 5050 com ngrok, Cloudflare Tunnel ou ferramenta equivalente.

Exemplo com ngrok:

```powershell
ngrok http 5050
```

No painel da Twilio Sandbox, configure **When a message comes in** para:

```txt
https://SEU-TUNEL.ngrok-free.app/webhooks/twilio/whatsapp
```

Método:

```txt
POST
```

## O Que Já Está Persistido

- Usuária identificada por telefone
- Consentimentos LGPD iniciais
- Nome de exibição
- Confirmação de maioridade
- Última menstruação, duração média do ciclo e duração média da menstruação
- Método contraceptivo opcional
- Ciclos e eventos de menstruação
- Sintomas, fluxo, humor e relação sexual
- Gravidez e eventos de gravidez
- Intenções pendentes durante onboarding
- Mensagens inbound/outbound com corpo desativado por padrão

## Comandos Úteis

Ver usuárias cadastradas:

```powershell
curl.exe http://localhost:5050/admin/users
```

Ver eventos de uma usuária:

```powershell
curl.exe http://localhost:5050/admin/users/ID_DA_USUARIA/events
```

Parar:

```powershell
docker compose down
```

Apagar banco local:

```powershell
docker compose down -v
```

## IA Via OpenAI API

A Luma usa OpenAI API tanto em desenvolvimento quanto em produção.

```txt
OPENAI_API_KEY=sk-...
OPENAI_MODEL=gpt-5.4-mini
```

A API usa a Responses API com saídas estruturadas para:

- extrair dados de cadastro;
- interpretar intenções livres;
- selecionar tools que o backend valida e executa;
- humanizar a resposta final da Luma quando não for guardrail fixo.

O backend continua autoritativo: a IA sugere a ação, mas a API valida LGPD, maioridade, limites médicos e escrita no banco.

Regra do produto: o sistema decide e valida; a IA interpreta e escreve.
