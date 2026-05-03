# Plano Final da V1 - Gravidez, Desenvolvimento do Bebê e Calendário

Última atualização: 03/05/2026.

Este documento descreve a etapa final funcional da Luma antes da V1.0.0. A etapa foi implementada com TDD e cobre validação do fluxo de gravidez, respostas sobre desenvolvimento do bebê, geração opcional de imagem educativa com OpenAI + Cloudflare R2 e calendário visual mensal na web.

## Status Atual

Implementado:

- Fluxo de gravidez validado para gravidez informada, semanas gestacionais, DUM, DPP, sangramento, sintomas, pré-natal e ultrassom.
- Base `BabyDevelopmentKnowledgeBase` com estimativas seguras de 4 a 42 semanas.
- Tool `get_baby_development`.
- Tool `generate_baby_size_image` com geração OpenAI Images e upload para Cloudflare R2 quando as credenciais estiverem configuradas.
- Bloqueio de geração de imagem para plano Básico.
- Fallback textual quando a geração de imagem ou o R2 não estiverem configurados.
- Serviço `CycleCalendarService`.
- Endpoint autenticado `GET /account/calendar?month=YYYY-MM`.
- Tela web `/perfil/{accountId}/calendario?month=YYYY-MM` com navegação de mês anterior/próximo.
- Link no perfil para abrir o calendário.
- Tool `get_cycle_calendar`, retornando link direto para o calendário web do mês solicitado.
- TwiML com suporte a `<Media>` para enviar imagem pelo WhatsApp dentro da janela permitida pelo Twilio.
- Variáveis novas no `.env`, `.env.example` e `docker-compose.yml`.

Validação executada:

- `dotnet test whatsapp-app/Luma.sln`: 123 testes passando.
- `npm run lint`: passando.
- `npm run build`: passando.
- `docker compose --env-file .env -f docker-compose.yml config --quiet`: passando.
- `docker compose --env-file .env -f docker-compose.yml build api web`: passando.

## Parte 1 - Gravidez

O sistema já cobre:

- detecção de gravidez informada pela usuária;
- criação ou atualização de gravidez ativa;
- cálculo por DUM;
- cálculo por semanas gestacionais;
- registro de data provável do parto;
- resposta para “de quantas semanas estou?”;
- resposta para “qual minha previsão de parto?”;
- registro de sangramento na gravidez com orientação segura;
- registro de sintomas de gravidez;
- registro de consulta pré-natal;
- registro de ultrassom.

Guardrails mantidos:

- A Luma não confirma diagnóstico de gravidez.
- A Luma não diz se sangramento é “normal”.
- A Luma não diagnostica sintomas.
- A Luma não indica tratamento.
- A Luma não substitui pré-natal, médico ou obstetra.
- Em sinais de alerta, a resposta orienta procurar atendimento médico.

Para depois da V1, pode ser adicionado um campo específico de “data em que descobriu a gravidez”, mas isso não é obrigatório para cálculo de semanas ou DPP.

## Parte 2 - Desenvolvimento do Bebê

A Luma responde perguntas como:

```txt
Qual o tamanho do meu bebê?
Como meu bebê está essa semana?
Com 12 semanas o bebê tem que tamanho?
O que acontece na semana 20?
```

Regras:

- Se a pergunta informar a semana, a Luma usa a semana informada.
- Se não informar semana, a Luma usa a gravidez ativa.
- Se faltar referência, a Luma pede DUM, semanas ou DPP.
- As respostas são sempre estimativas educativas.
- A Luma não usa termos de diagnóstico como “normal/anormal”.

Tools:

```txt
get_baby_development
generate_baby_size_image
```

Imagem educativa:

- OpenAI Images gera uma imagem sem texto, sem nudez, sem conteúdo alarmista e sem diagnóstico.
- A API salva a imagem no Cloudflare R2.
- A resposta pelo WhatsApp pode incluir mídia via TwiML `<Media>`.
- Se algo falhar, a Luma responde por texto.
- Recurso disponível apenas no plano Essencial.
- Usuárias do plano Básico recebem orientação para atualizar o plano no painel.

Cloudflare R2 configurado:

```txt
Bucket: luma
Prefixo: baby-image-generation/
Endpoint S3: https://52ed8799dba98c8a90611db618a89e3e.r2.cloudflarestorage.com
Public Development URL: https://pub-7621f98d02d741da84d6fd1b054da6d5.r2.dev
```

Importante:

- `R2_ENDPOINT` deve ser o endpoint S3 sem `/luma` no final.
- `R2_PUBLIC_BASE_URL` é a URL pública usada pela Twilio para baixar a imagem.
- Em produção, trocar o Public Development URL por um domínio customizado.
- Criar uma Lifecycle Rule no R2 para apagar objetos do prefixo `baby-image-generation/` depois de 1 dia.
- Não apagar a imagem imediatamente após enviar, porque Twilio/Meta pode buscar a mídia com atraso ou retry.

## Parte 3 - Calendário Visual

Endpoint:

```txt
GET /account/calendar?month=YYYY-MM
```

Autenticação:

- JWT/cookie da web.
- A usuária só acessa o próprio calendário.

Tela:

```txt
/perfil/{accountId}/calendario?month=YYYY-MM
```

O calendário mostra:

- início da menstruação registrado;
- dias menstruando registrados;
- fim da menstruação registrado;
- previsão de próxima menstruação;
- janela fértil estimada;
- ovulação estimada;
- relação sexual registrada;
- sintomas;
- humor;
- semanas de gravidez;
- semana prevista para parto.

Regras:

- O calendário busca apenas o mês solicitado.
- Dados antigos não são apagados automaticamente na V1.
- Previsões menstruais são ocultadas quando há gravidez ativa.
- Período fértil e ovulação são sempre estimativas, nunca método contraceptivo.

WhatsApp:

- A IA interpreta pedidos como “mostra meu calendário”, “calendário desse mês”, “calendário de maio” e “calendário do mês que vem”.
- O backend valida o mês e devolve link direto para o site.
- Para V1, o WhatsApp envia link web, não imagem mensal.

## Variáveis Novas

Adicionar no Render para a API:

```env
Luma__WebBaseUrl=https://seu-dominio-web
OpenAI__ImageModel=gpt-image-1
R2__AccountId=52ed8799dba98c8a90611db618a89e3e
R2__BucketName=luma
R2__AccessKeyId=SUA_ACCESS_KEY
R2__SecretAccessKey=SUA_SECRET_KEY
R2__Endpoint=https://52ed8799dba98c8a90611db618a89e3e.r2.cloudflarestorage.com
R2__PublicBaseUrl=https://pub-7621f98d02d741da84d6fd1b054da6d5.r2.dev
R2__BabyImagePrefix=baby-image-generation
R2__ImageRetentionDays=1
```

No `.env` local, os nomes equivalentes são:

```env
LUMA_WEB_BASE_URL=http://localhost:3000
OPENAI_IMAGE_MODEL=gpt-image-1
R2_ACCOUNT_ID=52ed8799dba98c8a90611db618a89e3e
R2_BUCKET_NAME=luma
R2_ACCESS_KEY_ID=
R2_SECRET_ACCESS_KEY=
R2_ENDPOINT=https://52ed8799dba98c8a90611db618a89e3e.r2.cloudflarestorage.com
R2_PUBLIC_BASE_URL=https://pub-7621f98d02d741da84d6fd1b054da6d5.r2.dev
R2_BABY_IMAGE_PREFIX=baby-image-generation
R2_IMAGE_RETENTION_DAYS=1
```

## Definição Final de V1.0.0

A V1.0.0 fica pronta quando:

- cadastro web e WhatsApp estiverem funcionais;
- assinatura Stripe liberar ou bloquear WhatsApp corretamente;
- ciclo menstrual estiver completo;
- gravidez estiver validada;
- desenvolvimento do bebê por semana estiver disponível;
- imagem educativa do bebê estiver configurada com R2 em ambiente desejado;
- plano Essencial liberar imagem e plano Básico bloquear imagem corretamente;
- calendário visual estiver disponível na web;
- pedidos de calendário pelo WhatsApp retornarem link direto;
- rate limit, Redis e bloqueio de grupos estiverem ativos;
- templates Twilio de notificações estiverem documentados e aprovados antes de ativar o worker em produção.
