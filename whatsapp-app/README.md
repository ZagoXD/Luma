# Luma WhatsApp Bot MVP

Backend inicial da Luma para cadastro conversacional pelo WhatsApp, usando:

- ASP.NET Core 8
- PostgreSQL
- Entity Framework Core
- Ollama
- Docker Compose
- Twilio WhatsApp Sandbox via webhook TwiML

## Subir localmente

Copie o arquivo de exemplo de ambiente:

```powershell
Copy-Item .env.example .env
```

Em geral, para desenvolvimento local, os valores padrão já funcionam. O arquivo `.env` não deve ser commitado.

```powershell
docker compose up -d --build
```

Na primeira subida, o Docker também baixa a imagem do Ollama e faz pull do modelo configurado em `OLLAMA_MODEL`. Isso pode demorar alguns minutos.

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

## Variáveis de ambiente

As variáveis ficam em `.env`, baseado em `.env.example`.

```txt
POSTGRES_DB=luma
POSTGRES_USER=luma
POSTGRES_PASSWORD=luma_dev_password
POSTGRES_HOST_PORT=5433
API_HOST_PORT=5050
ASPNETCORE_ENVIRONMENT=Development
LUMA_STORE_MESSAGE_BODIES=false
OLLAMA_ENABLED=true
OLLAMA_BASE_URL=http://ollama:11434
OLLAMA_MODEL=llama3.2
OLLAMA_TIMEOUT_SECONDS=20
OLLAMA_HOST_PORT=11434
```

No estado atual, não há segredo da Twilio no backend. A Twilio chama o webhook e a API responde com TwiML na própria requisição.

## Teste sem Twilio

Use o endpoint local de desenvolvimento:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5050/dev/messages `
  -ContentType 'application/json; charset=utf-8' `
  -Body (@{ from = '+5516992330309'; body = 'oi' } | ConvertTo-Json -Compress)
```

Depois siga o fluxo respondendo:

```txt
1
Nay
1
10/04
28
5
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

Depois envie uma mensagem no WhatsApp para o número da sandbox:

```txt
+1 415 523 8886
```

Se ainda não entrou na sandbox, envie antes:

```txt
join shall-list
```

## O que já está persistido

- Usuária identificada por telefone
- Consentimentos LGPD iniciais
- Nome de exibição
- Confirmação de maioridade
- Última menstruação, duração média do ciclo e duração média da menstruação
- Ciclos e eventos básicos
- Mensagens inbound/outbound com corpo desativado por padrão

## Comandos úteis

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

## IA local via Docker

O onboarding do MVP usa regras determinísticas com fallback para Ollama durante a coleta de dados. Isso permite interpretar mensagens naturais depois do consentimento, como:

```txt
Olá, meu nome é Julia
Oi, meu nome é Marina, tenho 25 anos e minha última menstruação foi 12/04
meu ciclo costuma ter 30 dias e a menstruação dura 5 dias
```

Por privacidade, a Luma ainda exige consentimento explícito antes de salvar qualquer dado da usuária.

O Ollama sobe junto no Docker Compose:

```txt
api -> http://ollama:11434
```

O serviço `ollama-pull` baixa automaticamente o modelo definido em:

```txt
OLLAMA_MODEL=llama3.2
```

Para verificar os modelos dentro do container:

```powershell
docker exec luma-ollama ollama list
```

Em produção, não exponha a porta `11434` publicamente. A API deve falar com o Ollama pela rede interna do Docker.

Próxima evolução: usar o Ollama também como `IMessageIntentParser` para mensagens livres após o cadastro e como `IResponseHumanizer`, mantendo a regra do produto: o sistema decide, a IA escreve.
