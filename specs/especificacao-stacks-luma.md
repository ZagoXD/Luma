# Luma â€” EspecificaÃ§Ã£o de Stacks e Arquitetura TÃ©cnica

> Atualização de escopo para V1.0.0 - 2026-04-28
>
> O projeto esta na fase final da V1: o backend transacional já cobre cadastro, ciclo menstrual, relação sexual, gravidez e guardrails principais. O que falta para a primeira versão de producao e a camada de inteligência conversacional com RAG e tools/MCP, mantendo o backend como autoridade.
>
> A arquitetura recomendada para a V1.0.0 passa a incluir um orquestrador de conversa: Ollama interpreta contexto e intencoes, RAG fornece conhecimento seguro, tools/MCP executam leituras/escritas controladas e o backend valida tudo antes de persistir ou responder.

---

## Atualização arquitetural - Orquestrador inteligente

A V1.0.0 deve adicionar uma camada acima do backend atual:

```txt
WhatsApp
  ->
LumaConversationOrchestrator
  ->
Ollama para interpretacao/contexto/humanizacao
  ->
RAG para conhecimento seguro
  ->
Tools internas ou MCP
  ->
Backend autoritativo
  ->
PostgreSQL
```

Componentes recomendados:

```txt
ILumaConversationOrchestrator
IConversationContextBuilder
IConversationIntentParser
IResponseHumanizer
IKnowledgeRetrievalService
IToolRegistry ou MCP Server
IPendingIntentService
ISafetyGuardrailService
```

Ferramentas controladas:

```txt
get_user_profile
get_onboarding_state
save_pending_intent
clear_pending_intent
complete_onboarding_step
record_period_start
record_period_end
record_flow_update
record_symptom
record_mood
record_sexual_activity
start_pregnancy_mode
record_pregnancy_bleeding
record_pregnancy_symptom
record_prenatal_appointment
record_ultrasound
calculate_next_period
calculate_delay
get_last_period
get_last_symptom
get_last_sexual_activity
search_luma_knowledge_base
```

Regras:

- A IA não escreve direto no banco.
- A IA solicita uma tool.
- O backend valida consentimento, estado, segurança, LGPD e regras médicas.
- O backend executa ou recusa.
- A IA humaniza a resposta final.
- Guardrails de LGPD e saúde continuam fixos no backend.

---

Este documento complementa a especificaÃ§Ã£o funcional do projeto **Luma**, uma futura assistente de ciclo menstrual pelo WhatsApp.  
O objetivo aqui Ã© separar as tecnologias recomendadas por etapa de desenvolvimento, mantendo uma arquitetura simples para validaÃ§Ã£o inicial, mas preparada para evoluir para uma plataforma real.

---

## 1. VisÃ£o geral da separaÃ§Ã£o do projeto

O projeto deve ser pensado em duas grandes frentes:

```txt
1. Site / Landing Page / Cadastro
   ResponsÃ¡vel por divulgar a ideia, captar interessadas, explicar a proposta e futuramente permitir cadastro/pagamento.

2. Plataforma Backend do Bot
   ResponsÃ¡vel pelo funcionamento real da assistente: WhatsApp, regras de ciclo, banco de dados, IA, lembretes, pagamentos e privacidade.
```

A ideia principal Ã© evitar misturar tudo em uma Ãºnica aplicaÃ§Ã£o logo no inÃ­cio. O site precisa ser rÃ¡pido de criar e publicar. A plataforma do bot precisa ser robusta, segura e organizada.

---

# Parte 1 â€” Site de divulgaÃ§Ã£o e cadastro

## 2. Objetivo do site

O site da Luma nÃ£o deve ser o aplicativo em si no primeiro momento. Ele deve servir para:

- divulgar a proposta do produto;
- explicar a dor que a Luma resolve;
- apresentar o conceito de acompanhamento do ciclo pelo WhatsApp;
- capturar leads para lista de espera;
- validar interesse real antes de construir o sistema completo;
- futuramente permitir cadastro, login e assinatura.

No MVP inicial, o site pode ser apenas uma landing page com formulÃ¡rio de interesse.

---

## 3. Stack recomendada para o site

### Stack principal

```txt
Next.js
TypeScript
Tailwind CSS
Vercel
Supabase ou formulÃ¡rio externo para lista de espera
```

### Por que essa stack?

**Next.js** Ã© uma excelente escolha para landing pages e produtos SaaS porque oferece boa performance, SEO, rotas, renderizaÃ§Ã£o hÃ­brida e facilidade de deploy.

**TypeScript** ajuda a manter o cÃ³digo seguro, escalÃ¡vel e menos propenso a erros.

**Tailwind CSS** permite criar uma interface moderna e responsiva rapidamente, com excelente controle visual.

**Vercel** Ã© uma opÃ§Ã£o natural para hospedar o site, especialmente se o frontend for feito em Next.js.

**Supabase**, **Tally**, **Formspree**, **Airtable** ou **Google Forms** podem ser usados para captar leads no inÃ­cio.

---

## 4. Estrutura sugerida do repositÃ³rio do site

```txt
/luma-site
  app/
    page.tsx
    obrigado/page.tsx
    política-de-privacidade/page.tsx
    termos/page.tsx
  components/
    Hero.tsx
    ProblemSection.tsx
    SolutionSection.tsx
    FeaturesSection.tsx
    HowItWorksSection.tsx
    PrivacySection.tsx
    WaitlistForm.tsx
    Footer.tsx
  lib/
    waitlist.ts
  public/
    images/
  styles/
```

---

## 5. PÃ¡ginas recomendadas para o site

### PÃ¡ginas iniciais

```txt
/
/obrigado
/política-de-privacidade
/termos
```

### PÃ¡ginas futuras

```txt
/login
/cadastro
/painel
/assinatura
/checkout
```

No inÃ­cio, `/login`, `/cadastro`, `/painel` e `/checkout` nÃ£o sÃ£o necessÃ¡rios. Eles podem ser adicionados quando o produto sair da fase de validaÃ§Ã£o.

---

## 6. FormulÃ¡rio de lista de espera

Campos recomendados:

```txt
Nome
E-mail
WhatsApp
Maior dificuldade com apps de ciclo
Checkbox de consentimento para contato futuro
Data de criaÃ§Ã£o
Origem da campanha
```

Exemplo de tabela `waitlist_leads`:

```sql
CREATE TABLE waitlist_leads (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name TEXT NOT NULL,
  email TEXT,
  phone TEXT,
  main_pain TEXT,
  consent_contact BOOLEAN NOT NULL DEFAULT FALSE,
  source TEXT,
  created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
```

---

## 7. OpÃ§Ãµes para captura de leads

### OpÃ§Ã£o mais rÃ¡pida

```txt
Tally
Typeform
Google Forms
Formspree
```

Boa para validar sem criar backend.

### OpÃ§Ã£o mais profissional

```txt
Supabase
```

Boa para jÃ¡ guardar leads em PostgreSQL e evoluir depois para cadastro real.

### RecomendaÃ§Ã£o inicial

Para MVP rÃ¡pido:

```txt
Next.js + Tally/Formspree
```

Para MVP mais preparado:

```txt
Next.js + Supabase
```

---

## 8. Pagamento futuro no site

Quando a Luma comeÃ§ar a cobrar assinatura, algumas opÃ§Ãµes sÃ£o:

```txt
Stripe Billing
Mercado Pago
Asaas
Pagar.me
```

### RecomendaÃ§Ã£o

Para mercado brasileiro, considerar:

```txt
Asaas ou Mercado Pago
```

Para arquitetura SaaS mais padronizada e internacional:

```txt
Stripe Billing
```

No MVP, o pagamento nÃ£o precisa existir. A prioridade deve ser captar interessadas e validar se elas pagariam pelo serviÃ§o.

---

# Parte 2 â€” Plataforma Backend do Bot

## 9. Objetivo da plataforma backend

A plataforma backend serÃ¡ responsÃ¡vel por:

- receber mensagens do WhatsApp;
- identificar a usuÃ¡ria pelo nÃºmero de telefone;
- validar assinatura ativa;
- interpretar mensagens;
- registrar eventos do ciclo;
- calcular previsÃµes;
- responder de forma segura;
- integrar com IA;
- enviar lembretes;
- controlar limites de uso;
- armazenar consentimentos;
- permitir exclusÃ£o/exportaÃ§Ã£o de dados;
- processar webhooks de pagamento;
- manter logs e auditoria.

Essa Ã© a parte mais sensÃ­vel e importante do projeto.

---

## 10. Stack recomendada para o backend

### Stack principal

```txt
ASP.NET Core Web API
C#
PostgreSQL
Entity Framework Core
Redis
Hangfire
Docker
Gemini API
WhatsApp API ou provedor terceiro
```

### Por que C# aqui?

C# Ã© uma Ã³tima escolha para a plataforma backend da Luma porque o sistema terÃ¡ muitas regras de negÃ³cio, integraÃ§Ãµes, jobs, webhooks e necessidade de seguranÃ§a.

O backend do bot exige:

```txt
validaÃ§Ã£o de webhook
controle de assinatura
rate limit
logs estruturados
regras de ciclo menstrual
cÃ¡lculos de previsÃ£o
jobs de lembrete
integraÃ§Ã£o com WhatsApp
integraÃ§Ã£o com Gemini
consentimento LGPD
auditoria
criptografia
```

Essas responsabilidades combinam muito bem com **ASP.NET Core**.

---

## 11. Arquitetura recomendada

Para comeÃ§ar, a melhor opÃ§Ã£o Ã© um **monÃ³lito modular** em C#.

Evitar microserviÃ§os no inÃ­cio. O produto ainda estarÃ¡ validando mercado, entÃ£o microserviÃ§os adicionariam complexidade desnecessÃ¡ria.

### Arquitetura MVP

```txt
[Landing Page - Next.js]
        â†“
[Lista de espera / Cadastro]
        â†“
[Backend ASP.NET Core]
        â†“
[PostgreSQL]
        â†“
[WhatsApp API]
        â†“
[Gemini API]
```

### Arquitetura com filas/jobs

```txt
WhatsApp
  â†“
Webhook ASP.NET Core
  â†“
ValidaÃ§Ã£o da mensagem
  â†“
Fila / Job
  â†“
Processador de mensagem
  â†“
Motor de ciclo
  â†“
Banco de dados
  â†“
Gemini para humanizaÃ§Ã£o
  â†“
Resposta via WhatsApp
```

---

## 12. Estrutura sugerida do backend

```txt
/luma-platform
  src/
    Luma.Api/
    Luma.Application/
    Luma.Domain/
    Luma.Infrastructure/
    Luma.Worker/
  tests/
    Luma.Tests/
```

---

## 13. Responsabilidade de cada camada

## `Luma.Api`

ResponsÃ¡vel por expor endpoints HTTP.

```txt
controllers
webhooks
autenticaÃ§Ã£o
endpoints administrativos
health checks
Swagger/OpenAPI
```

Endpoints iniciais:

```txt
POST /webhooks/whatsapp
POST /webhooks/payment
GET /health
GET /admin/users
GET /admin/conversations
```

---

## `Luma.Domain`

ResponsÃ¡vel pelas entidades e regras puras do negÃ³cio.

Entidades principais:

```txt
User
UserPreference
Cycle
CycleEvent
Pregnancy
Subscription
Message
Reminder
Consent
AuditLog
```

Regras de domÃ­nio:

```txt
abrir ciclo
encerrar ciclo
registrar intensidade
registrar sintoma
calcular prÃ³xima menstruaÃ§Ã£o
calcular atraso
validar se resposta Ã© segura
bloquear diagnÃ³sticos
```

---

## `Luma.Application`

ResponsÃ¡vel pelos casos de uso.

Exemplos:

```txt
HandleIncomingMessageUseCase
RegisterPeriodStartUseCase
RegisterPeriodEndUseCase
RegisterFlowUpdateUseCase
RegisterSymptomUseCase
GenerateBotReplyUseCase
CreateSubscriptionUseCase
CancelSubscriptionUseCase
ExportUserDataUseCase
DeleteUserDataUseCase
```

---

## `Luma.Infrastructure`

ResponsÃ¡vel por integraÃ§Ãµes externas e persistÃªncia.

```txt
PostgreSQL
Entity Framework Core
WhatsApp Provider
Gemini Provider
Payment Provider
Email Provider
Redis
Storage
```

---

## `Luma.Worker`

ResponsÃ¡vel por tarefas em segundo plano.

```txt
enviar lembretes
processar mensagens pendentes
recalcular previsÃµes
verificar assinaturas vencidas
limpar sessÃµes temporÃ¡rias
executar jobs agendados
```

---

# Parte 3 â€” Banco de dados

## 14. Banco recomendado

```txt
PostgreSQL
```

PostgreSQL Ã© uma boa escolha porque Ã© robusto, barato, amplamente suportado e permite usar tanto dados relacionais quanto campos flexÃ­veis com `JSONB`.

---

## 15. Tabelas principais

```txt
users
user_preferences
cycles
cycle_events
pregnancies
messages
subscriptions
reminders
consents
audit_logs
```

---

## 16. Modelo de eventos do ciclo

A tabela mais importante do produto serÃ¡ `cycle_events`.

Ela permite registrar diferentes tipos de eventos sem precisar alterar o banco a cada nova funcionalidade.

```sql
CREATE TABLE cycle_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,
  cycle_id UUID,
  type TEXT NOT NULL,
  date DATE NOT NULL,
  source TEXT NOT NULL,
  metadata JSONB,
  created_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
```

Tipos de eventos:

```txt
period_start
period_end
flow_update
symptom
mood
sexual_activity
contraceptive_taken
contraceptive_missed
pregnancy_positive
pregnancy_bleeding
pregnancy_symptom
note
```

---

## 17. Exemplo de evento

```json
{
  "id": "event_123",
  "user_id": "user_123",
  "cycle_id": "cycle_123",
  "type": "flow_update",
  "date": "2026-04-24",
  "source": "whatsapp",
  "metadata": {
    "flow_intensity": "medium"
  },
  "created_at": "2026-04-24T20:10:00Z"
}
```

---

# Parte 4 â€” WhatsApp

## 18. OpÃ§Ãµes de integraÃ§Ã£o com WhatsApp

Existem duas abordagens principais.

---

## OpÃ§Ã£o A â€” WhatsApp Cloud API oficial

Vantagens:

```txt
mais oficial
mais controle
melhor para escalar
menos dependÃªncia de intermediÃ¡rio
```

Desvantagens:

```txt
configuraÃ§Ã£o inicial mais burocrÃ¡tica
exige configurar app, nÃºmero, webhooks e templates
```

---

## OpÃ§Ã£o B â€” Provedor terceiro

Exemplos:

```txt
Z-API
Twilio
360dialog
Evolution API
outros provedores de WhatsApp
```

Vantagens:

```txt
mais rÃ¡pido para MVP
painel pronto
integraÃ§Ã£o simplificada
```

Desvantagens:

```txt
mensalidade fixa
markup nas mensagens
dependÃªncia do provedor
risco de limitaÃ§Ãµes futuras
```

---

## 19. RecomendaÃ§Ã£o para WhatsApp

Para MVP:

```txt
comeÃ§ar com um provedor terceiro mais simples
```

Para produto sÃ©rio em escala:

```txt
migrar para WhatsApp Cloud API oficial
```

A arquitetura deve usar uma interface para desacoplar o provedor:

```csharp
public interface IWhatsAppClient
{
    Task SendTextMessageAsync(string phoneNumber, string message);
}
```

Assim, Ã© possÃ­vel comeÃ§ar com Z-API ou outro provedor e trocar depois sem reescrever o sistema inteiro.

---

# Parte 5 â€” Gemini e IA

## 20. Papel da IA no sistema

A IA nÃ£o deve ser o cÃ©rebro mÃ©dico do produto.

Regra principal:

```txt
O sistema decide. A IA escreve.
```

Ou seja:

- a IA pode interpretar mensagens;
- a IA pode transformar linguagem natural em JSON estruturado;
- a IA pode humanizar respostas;
- a IA pode responder dÃºvidas gerais com guardrails;
- a IA nÃ£o deve diagnosticar;
- a IA nÃ£o deve afirmar gravidez;
- a IA nÃ£o deve sugerir condutas mÃ©dicas arriscadas;
- a IA nÃ£o deve decidir se algo Ã© normal ou seguro.

---

## 21. ServiÃ§os de IA recomendados

```txt
IMessageIntentParser
IResponseHumanizer
ISafetyGuardrailService
```

### `IMessageIntentParser`

Transforma mensagem natural em intenÃ§Ã£o estruturada.

Exemplo:

Mensagem:

```txt
desceu ontem e hoje tÃ¡ forte
```

Resposta esperada:

```json
{
  "intent": "period_start",
  "date": "2026-04-23",
  "flow": "intense",
  "confidence": 0.91
}
```

---

### `IResponseHumanizer`

Transforma uma resposta determinÃ­stica em uma mensagem mais natural.

Entrada:

```json
{
  "action": "period_start_created",
  "date": "2026-04-23",
  "flow": "intense"
}
```

SaÃ­da:

```txt
Registrei que sua menstruaÃ§Ã£o comeÃ§ou ontem com fluxo intenso âœ…
```

---

### `ISafetyGuardrailService`

Bloqueia ou redireciona respostas sensÃ­veis.

Exemplos de mensagens que devem ser bloqueadas ou tratadas com cuidado:

```txt
Estou grÃ¡vida?
Esse sangramento Ã© normal?
Posso ter relaÃ§Ã£o sem proteÃ§Ã£o hoje?
Acho que estou com infecÃ§Ã£o.
```

Resposta segura:

```txt
NÃ£o consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnÃ³stico ou decisÃ£o mÃ©dica o ideal Ã© procurar um profissional de saÃºde.
```

---

# Parte 6 â€” Redis, filas e jobs

## 22. Redis

Redis pode ser usado para:

```txt
rate limit
controle de spam
cache
estado temporÃ¡rio da conversa
fila simples
bloqueio temporÃ¡rio por abuso
```

Exemplo de regra:

```txt
5 mensagens em menos de 10 segundos â†’ timeout temporÃ¡rio
```

---

## 23. Jobs em background

Para jobs em C#, usar:

```txt
Hangfire
```

Alternativa:

```txt
Quartz.NET
```

### RecomendaÃ§Ã£o

Para MVP:

```txt
Hangfire
```

Motivo: simples, prÃ¡tico, possui dashboard e funciona bem para tarefas recorrentes.

Jobs possÃ­veis:

```txt
enviar lembrete da prÃ³xima menstruaÃ§Ã£o
lembrar de registrar sintomas
verificar assinaturas vencidas
processar mensagens pendentes
limpar sessÃµes antigas
recalcular previsÃµes
```

---

# Parte 7 â€” AutenticaÃ§Ã£o e usuÃ¡rios

## 24. Identidade inicial

No inÃ­cio, a identidade principal da usuÃ¡ria pode ser o nÃºmero de WhatsApp.

```txt
phone_number = identificador principal
```

Mas futuramente Ã© recomendÃ¡vel ter login para:

```txt
acessar painel
exportar dados
apagar conta
ver histÃ³rico
alterar assinatura
configurar lembretes
```

---

## 25. OpÃ§Ãµes de autenticaÃ§Ã£o

### OpÃ§Ã£o C# nativa

```txt
ASP.NET Identity + JWT
```

### OpÃ§Ãµes externas

```txt
Clerk
Auth0
Supabase Auth
Firebase Auth
```

### RecomendaÃ§Ã£o

Para manter a plataforma coesa em C#:

```txt
ASP.NET Identity + JWT
```

Para acelerar MVP com menos backend de autenticaÃ§Ã£o:

```txt
Clerk ou Supabase Auth
```

---

# Parte 8 â€” LGPD, privacidade e seguranÃ§a

## 26. Dados sensÃ­veis

A Luma lidarÃ¡ com dados extremamente sensÃ­veis, como:

```txt
menstruaÃ§Ã£o
sintomas
humor
vida sexual
gravidez
sangramentos
uso de anticoncepcional
```

Por isso, a arquitetura deve considerar privacidade desde o inÃ­cio.

---

## 27. Funcionalidades mÃ­nimas de privacidade

O sistema deve permitir:

```txt
consentimento explÃ­cito
registro do consentimento
revogaÃ§Ã£o de consentimento
exportaÃ§Ã£o de dados
exclusÃ£o de dados
polÃ­tica de privacidade clara
termos de uso
limitaÃ§Ã£o de finalidade
logs sem conteÃºdo sensÃ­vel quando possÃ­vel
criptografia em repouso quando possÃ­vel
criptografia em trÃ¢nsito
controle de acesso administrativo
```

---

## 28. Tabela de consentimentos

```sql
CREATE TABLE consents (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL,
  consent_type TEXT NOT NULL,
  accepted BOOLEAN NOT NULL,
  accepted_at TIMESTAMP WITH TIME ZONE DEFAULT now(),
  ip_address TEXT,
  user_agent TEXT,
  version TEXT
);
```

Tipos de consentimento:

```txt
privacy_policy
terms_of_use
health_data_processing
marketing_contact
whatsapp_contact
```

---

# Parte 9 â€” Deploy e infraestrutura

## 29. Deploy inicial recomendado

### Site

```txt
Vercel
```

### Backend

OpÃ§Ãµes:

```txt
Render
Railway
Fly.io
DigitalOcean VPS
Azure App Service
```

### Banco

OpÃ§Ãµes:

```txt
Supabase Postgres
Neon
Railway Postgres
DigitalOcean Managed PostgreSQL
```

### Redis

OpÃ§Ãµes:

```txt
Upstash
Redis Cloud
Railway Redis
Redis em Docker no VPS
```

---

## 30. Deploy MVP barato

```txt
Site: Vercel
Backend: Render/Railway/Fly.io
Banco: Supabase ou Neon
Redis: Upstash
```

---

## 31. Deploy com mais controle

```txt
Site: Vercel
Backend: VPS DigitalOcean com Docker Compose
Banco: PostgreSQL no VPS ou gerenciado
Redis: Docker no VPS
```

---

## 32. Deploy produÃ§Ã£o mais sÃ©rio

```txt
Site: Vercel
Backend: Azure App Service, Azure Container Apps, AWS ECS ou DigitalOcean App Platform
Banco: PostgreSQL gerenciado
Redis gerenciado
Observabilidade: Sentry + OpenTelemetry
```

---

# Parte 10 â€” Observabilidade e qualidade

## 33. Logs

Para logs em C#:

```txt
Serilog
```

Usar logs estruturados, evitando registrar conteÃºdo sensÃ­vel das mensagens.

Exemplo de log bom:

```json
{
  "event": "message_received",
  "user_id": "user_123",
  "intent": "period_start",
  "provider": "whatsapp",
  "timestamp": "2026-04-24T20:00:00Z"
}
```

Evitar logar:

```txt
conteÃºdo completo da mensagem
sintomas Ã­ntimos detalhados
informaÃ§Ãµes sexuais
nÃºmero completo de telefone sem mascaramento
```

---

## 34. Erros

Ferramentas recomendadas:

```txt
Sentry
OpenTelemetry
Application Insights
```

Para MVP:

```txt
Sentry + Serilog
```

---

## 35. DocumentaÃ§Ã£o da API

Usar:

```txt
Swagger / OpenAPI
```

ASP.NET Core possui integraÃ§Ã£o fÃ¡cil com Swagger.

---

## 36. Testes

Ferramentas recomendadas:

```txt
xUnit
FluentAssertions
Testcontainers
Moq ou NSubstitute
```

### Testes importantes

```txt
cÃ¡lculo de ciclo
cÃ¡lculo de atraso
registro de inÃ­cio/fim de menstruaÃ§Ã£o
alteraÃ§Ã£o de intensidade
bloqueio de diagnÃ³stico
rate limit
webhook do WhatsApp
webhook de pagamento
exclusÃ£o de dados
```

---

# Parte 11 â€” Fases de desenvolvimento

## 37. Fase 1 â€” Landing page

### Objetivo

Validar interesse.

### Stack

```txt
Next.js
TypeScript
Tailwind CSS
Vercel
Tally/Formspree ou Supabase
```

### Entregas

```txt
landing page
formulÃ¡rio de lista de espera
pÃ¡gina de obrigado
polÃ­tica de privacidade inicial
termos iniciais
```

---

## 38. Fase 2 â€” PrÃ©-cadastro e validaÃ§Ã£o comercial

### Objetivo

Medir intenÃ§Ã£o real de uso e pagamento.

### Stack

```txt
Next.js
Supabase/Postgres
E-mail transacional
Stripe/Mercado Pago/Asaas opcional
```

### Entregas

```txt
lista de espera persistente
segmentaÃ§Ã£o de leads
campanhas de e-mail/WhatsApp autorizadas
pesquisa de interesse
possÃ­vel prÃ©-venda
```

---

## 39. Fase 3 â€” Bot MVP

### Objetivo

Criar a primeira versÃ£o funcional da Luma pelo WhatsApp.

### Stack

```txt
ASP.NET Core Web API
PostgreSQL
Entity Framework Core
WhatsApp Provider
Gemini API
Hangfire
Redis
Docker
```

### Entregas

```txt
receber mensagens
identificar usuÃ¡ria
registrar inÃ­cio da menstruaÃ§Ã£o
registrar fim da menstruaÃ§Ã£o
registrar intensidade
registrar sintomas
responder perguntas simples
calcular prÃ³xima menstruaÃ§Ã£o
calcular atraso
limitar spam
registrar consentimento
```

---

## 40. Fase 4 â€” Painel/admin

### Objetivo

Gerenciar operaÃ§Ã£o, suporte e visualizaÃ§Ã£o bÃ¡sica.

### OpÃ§Ãµes de stack

```txt
Next.js Admin
Blazor
ASP.NET Core MVC
```

### RecomendaÃ§Ã£o

Para velocidade e consistÃªncia visual:

```txt
Next.js Admin
```

Para aplicar ainda mais C#:

```txt
Blazor
```

### Entregas

```txt
visualizar usuÃ¡rias
visualizar status de assinatura
consultar logs nÃ£o sensÃ­veis
acompanhar mensagens processadas
reenviar mensagens com falha
bloquear/desbloquear usuÃ¡rias
```

---

## 41. Fase 5 â€” Plataforma completa

### Objetivo

Transformar o MVP em produto real.

### Entregas

```txt
assinaturas recorrentes
painel da usuÃ¡ria
exportaÃ§Ã£o de dados
exclusÃ£o de conta
lembretes configurÃ¡veis
modo gravidez opcional
relatÃ³rios de ciclo
melhorias de IA
mÃ©tricas de uso
observabilidade avanÃ§ada
```

---

# Parte 12 â€” Stack final recomendada

## 42. Site

```txt
Next.js
TypeScript
Tailwind CSS
Vercel
Supabase ou Tally/Formspree para leads
```

---

## 43. Backend do bot

```txt
ASP.NET Core Web API
C#
PostgreSQL
Entity Framework Core
Redis
Hangfire
Docker
Gemini API
WhatsApp Provider
```

---

## 44. Painel administrativo futuro

```txt
Next.js Admin
```

ou

```txt
Blazor
```

---

## 45. Pagamentos futuros

```txt
Asaas
Mercado Pago
Stripe Billing
Pagar.me
```

---

## 46. Observabilidade

```txt
Serilog
Sentry
OpenTelemetry
Swagger/OpenAPI
```

---

## 47. Testes

```txt
xUnit
FluentAssertions
Testcontainers
```

---

# Parte 13 â€” DecisÃ£o final sugerida

A recomendaÃ§Ã£o final Ã©:

```txt
Luma Site
Next.js + TypeScript + Tailwind CSS + Vercel

Luma Platform
ASP.NET Core Web API + PostgreSQL + Entity Framework Core + Redis + Hangfire + Docker

IA
Gemini como parser e humanizador, nÃ£o como tomador de decisÃ£o mÃ©dica

WhatsApp
Provedor terceiro no MVP, com arquitetura preparada para migrar para Cloud API oficial

Banco
PostgreSQL com modelo de eventos usando JSONB
```

---

## 48. Regra arquitetural mais importante

```txt
O sistema decide. A IA escreve.
```

Essa regra deve guiar todo o desenvolvimento da Luma.

A IA pode ajudar a interpretar e humanizar, mas as decisÃµes importantes devem estar no backend, em regras determinÃ­sticas, testÃ¡veis e auditÃ¡veis.

---

## 49. Resumo executivo

A Luma deve ser construÃ­da em duas frentes:

```txt
1. Um site simples, bonito e rÃ¡pido para validar interesse.
2. Um backend robusto em C# para operar o bot de forma segura.
```

A escolha de **C# com ASP.NET Core** para o backend Ã© tecnicamente adequada e estratÃ©gica, porque permite criar uma plataforma bem estruturada, com regras de negÃ³cio, jobs, webhooks, integraÃ§Ãµes e privacidade desde o inÃ­cio.

O site deve continuar simples, usando tecnologias de frontend modernas e rÃ¡pidas, enquanto o backend concentra a complexidade real do produto.

