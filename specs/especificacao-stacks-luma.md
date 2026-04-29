# Luma — Especificação de Stacks e Arquitetura Técnica

> Atualização de escopo para V1.0.0 - 2026-04-28
>
> O projeto está na fase final da V1: o backend transacional já cobre cadastro, ciclo menstrual, relação sexual, gravidez e guardrails principais. O que falta para a primeira versão de produção e a camada de inteligência conversacional com RAG e tools/MCP, mantendo o backend como autoridade.
>
> A arquitetura recomendada para a V1.0.0 passa a incluir um orquestrador de conversa: Ollama interpreta contexto e intenções, RAG fornece conhecimento seguro, tools/MCP executam leituras/escritas controladas e o backend valida tudo antes de persistir ou responder.

---

## Atualização arquitetural - Orquestrador inteligente

A V1.0.0 deve adicionar uma camada acima do backend atual:

```txt
WhatsApp
  ->
LumaConversationOrchestrator
  ->
Ollama para interpretação/contexto/humanização
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

Este documento complementa a especificação funcional do projeto **Luma**, uma futura assistente de ciclo menstrual pelo WhatsApp.  
O objetivo aqui é separar as tecnologias recomendadas por etapa de desenvolvimento, mantendo uma arquitetura simples para validação inicial, mas preparada para evoluir para uma plataforma real.

---

## 1. Visão geral da separação do projeto

O projeto deve ser pensado em duas grandes frentes:

```txt
1. Site / Landing Page / Cadastro
   Responsável por divulgar a ideia, captar interessadas, explicar a proposta e futuramente permitir cadastro/pagamento.

2. Plataforma Backend do Bot
   Responsável pelo funcionamento real da assistente: WhatsApp, regras de ciclo, banco de dados, IA, lembretes, pagamentos e privacidade.
```

A ideia principal é evitar misturar tudo em uma única aplicação logo no início. O site precisa ser rápido de criar e publicar. A plataforma do bot precisa ser robusta, segura e organizada.

---

# Parte 1 — Site de divulgação e cadastro

## 2. Objetivo do site

O site da Luma não deve ser o aplicativo em si no primeiro momento. Ele deve servir para:

- divulgar a proposta do produto;
- explicar a dor que a Luma resolve;
- apresentar o conceito de acompanhamento do ciclo pelo WhatsApp;
- capturar leads para lista de espera;
- validar interesse real antes de construir o sistema completo;
- futuramente permitir cadastro, login e assinatura.

No MVP inicial, o site pode ser apenas uma landing page com formulário de interesse.

---

## 3. Stack recomendada para o site

### Stack principal

```txt
Next.js
TypeScript
Tailwind CSS
Vercel
Supabase ou formulário externo para lista de espera
```

### Por que essa stack?

**Next.js** é uma excelente escolha para landing pages e produtos SaaS porque oferece boa performance, SEO, rotas, renderização híbrida e facilidade de deploy.

**TypeScript** ajuda a manter o código seguro, escalável e menos propenso a erros.

**Tailwind CSS** permite criar uma interface moderna e responsiva rapidamente, com excelente controle visual.

**Vercel** é uma opção natural para hospedar o site, especialmente se o frontend for feito em Next.js.

**Supabase**, **Tally**, **Formspree**, **Airtable** ou **Google Forms** podem ser usados para captar leads no início.

---

## 4. Estrutura sugerida do repositório do site

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

## 5. Páginas recomendadas para o site

### Páginas iniciais

```txt
/
/obrigado
/política-de-privacidade
/termos
```

### Páginas futuras

```txt
/login
/cadastro
/painel
/assinatura
/checkout
```

No início, `/login`, `/cadastro`, `/painel` e `/checkout` não são necessários. Eles podem ser adicionados quando o produto sair da fase de validação.

---

## 6. Formulário de lista de espera

Campos recomendados:

```txt
Nome
E-mail
WhatsApp
Maior dificuldade com apps de ciclo
Checkbox de consentimento para contato futuro
Data de criação
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

## 7. Opções para captura de leads

### Opção mais rápida

```txt
Tally
Typeform
Google Forms
Formspree
```

Boa para validar sem criar backend.

### Opção mais profissional

```txt
Supabase
```

Boa para já guardar leads em PostgreSQL e evoluir depois para cadastro real.

### Recomendação inicial

Para MVP rápido:

```txt
Next.js + Tally/Formspree
```

Para MVP mais preparado:

```txt
Next.js + Supabase
```

---

## 8. Pagamento futuro no site

Quando a Luma começar a cobrar assinatura, algumas opções são:

```txt
Stripe Billing
Mercado Pago
Asaas
Pagar.me
```

### Recomendação

Para mercado brasileiro, considerar:

```txt
Asaas ou Mercado Pago
```

Para arquitetura SaaS mais padronizada e internacional:

```txt
Stripe Billing
```

No MVP, o pagamento não precisa existir. A prioridade deve ser captar interessadas e validar se elas pagariam pelo serviço.

---

# Parte 2 — Plataforma Backend do Bot

## 9. Objetivo da plataforma backend

A plataforma backend será responsável por:

- receber mensagens do WhatsApp;
- identificar a usuária pelo número de telefone;
- validar assinatura ativa;
- interpretar mensagens;
- registrar eventos do ciclo;
- calcular previsões;
- responder de forma segura;
- integrar com IA;
- enviar lembretes;
- controlar limites de uso;
- armazenar consentimentos;
- permitir exclusão/exportação de dados;
- processar webhooks de pagamento;
- manter logs e auditoria.

Essa é a parte mais sensível e importante do projeto.

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

C# é uma ótima escolha para a plataforma backend da Luma porque o sistema terá muitas regras de negócio, integrações, jobs, webhooks e necessidade de segurança.

O backend do bot exige:

```txt
validação de webhook
controle de assinatura
rate limit
logs estruturados
regras de ciclo menstrual
cálculos de previsão
jobs de lembrete
integração com WhatsApp
integração com Gemini
consentimento LGPD
auditoria
criptografia
```

Essas responsabilidades combinam muito bem com **ASP.NET Core**.

---

## 11. Arquitetura recomendada

Para começar, a melhor opção é um **monólito modular** em C#.

Evitar microserviços no início. O produto ainda estará validando mercado, então microserviços adicionariam complexidade desnecessária.

### Arquitetura MVP

```txt
[Landing Page - Next.js]
        ↓
[Lista de espera / Cadastro]
        ↓
[Backend ASP.NET Core]
        ↓
[PostgreSQL]
        ↓
[WhatsApp API]
        ↓
[Gemini API]
```

### Arquitetura com filas/jobs

```txt
WhatsApp
  ↓
Webhook ASP.NET Core
  ↓
Validação da mensagem
  ↓
Fila / Job
  ↓
Processador de mensagem
  ↓
Motor de ciclo
  ↓
Banco de dados
  ↓
Gemini para humanização
  ↓
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

Responsável por expor endpoints HTTP.

```txt
controllers
webhooks
autenticação
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

Responsável pelas entidades e regras puras do negócio.

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

Regras de domínio:

```txt
abrir ciclo
encerrar ciclo
registrar intensidade
registrar sintoma
calcular próxima menstruação
calcular atraso
validar se resposta é segura
bloquear diagnósticos
```

---

## `Luma.Application`

Responsável pelos casos de uso.

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

Responsável por integrações externas e persistência.

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

Responsável por tarefas em segundo plano.

```txt
enviar lembretes
processar mensagens pendentes
recalcular previsões
verificar assinaturas vencidas
limpar sessões temporárias
executar jobs agendados
```

---

# Parte 3 — Banco de dados

## 14. Banco recomendado

```txt
PostgreSQL
```

PostgreSQL é uma boa escolha porque é robusto, barato, amplamente suportado e permite usar tanto dados relacionais quanto campos flexíveis com `JSONB`.

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

A tabela mais importante do produto será `cycle_events`.

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

# Parte 4 — WhatsApp

## 18. Opções de integração com WhatsApp

Existem duas abordagens principais.

---

## Opção A — WhatsApp Cloud API oficial

Vantagens:

```txt
mais oficial
mais controle
melhor para escalar
menos dependência de intermediário
```

Desvantagens:

```txt
configuração inicial mais burocrática
exige configurar app, número, webhooks e templates
```

---

## Opção B — Provedor terceiro

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
mais rápido para MVP
painel pronto
integração simplificada
```

Desvantagens:

```txt
mensalidade fixa
markup nas mensagens
dependência do provedor
risco de limitações futuras
```

---

## 19. Recomendação para WhatsApp

Para MVP:

```txt
começar com um provedor terceiro mais simples
```

Para produto sério em escala:

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

Assim, é possível começar com Z-API ou outro provedor e trocar depois sem reescrever o sistema inteiro.

---

# Parte 5 — Gemini e IA

## 20. Papel da IA no sistema

A IA não deve ser o cérebro médico do produto.

Regra principal:

```txt
O sistema decide. A IA escreve.
```

Ou seja:

- a IA pode interpretar mensagens;
- a IA pode transformar linguagem natural em JSON estruturado;
- a IA pode humanizar respostas;
- a IA pode responder dúvidas gerais com guardrails;
- a IA não deve diagnosticar;
- a IA não deve afirmar gravidez;
- a IA não deve sugerir condutas médicas arriscadas;
- a IA não deve decidir se algo é normal ou seguro.

---

## 21. Serviços de IA recomendados

```txt
IMessageIntentParser
IResponseHumanizer
ISafetyGuardrailService
```

### `IMessageIntentParser`

Transforma mensagem natural em intenção estruturada.

Exemplo:

Mensagem:

```txt
desceu ontem e hoje tá forte
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

Transforma uma resposta determinística em uma mensagem mais natural.

Entrada:

```json
{
  "action": "period_start_created",
  "date": "2026-04-23",
  "flow": "intense"
}
```

Saída:

```txt
Registrei que sua menstruação começou ontem com fluxo intenso ✅
```

---

### `ISafetyGuardrailService`

Bloqueia ou redireciona respostas sensíveis.

Exemplos de mensagens que devem ser bloqueadas ou tratadas com cuidado:

```txt
Estou grávida?
Esse sangramento é normal?
Posso ter relação sem proteção hoje?
Acho que estou com infecção.
```

Resposta segura:

```txt
Não consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnóstico ou decisão médica o ideal é procurar um profissional de saúde.
```

---

# Parte 6 — Redis, filas e jobs

## 22. Redis

Redis pode ser usado para:

```txt
rate limit
controle de spam
cache
estado temporário da conversa
fila simples
bloqueio temporário por abuso
```

Exemplo de regra:

```txt
5 mensagens em menos de 10 segundos → timeout temporário
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

### Recomendação

Para MVP:

```txt
Hangfire
```

Motivo: simples, prático, possui dashboard e funciona bem para tarefas recorrentes.

Jobs possíveis:

```txt
enviar lembrete da próxima menstruação
lembrar de registrar sintomas
verificar assinaturas vencidas
processar mensagens pendentes
limpar sessões antigas
recalcular previsões
```

---

# Parte 7 — Autenticação e usuários

## 24. Identidade inicial

No início, a identidade principal da usuária pode ser o número de WhatsApp.

```txt
phone_number = identificador principal
```

Mas futuramente é recomendável ter login para:

```txt
acessar painel
exportar dados
apagar conta
ver histórico
alterar assinatura
configurar lembretes
```

---

## 25. Opções de autenticação

### Opção C# nativa

```txt
ASP.NET Identity + JWT
```

### Opções externas

```txt
Clerk
Auth0
Supabase Auth
Firebase Auth
```

### Recomendação

Para manter a plataforma coesa em C#:

```txt
ASP.NET Identity + JWT
```

Para acelerar MVP com menos backend de autenticação:

```txt
Clerk ou Supabase Auth
```

---

# Parte 8 — LGPD, privacidade e segurança

## 26. Dados sensíveis

A Luma lidará com dados extremamente sensíveis, como:

```txt
menstruação
sintomas
humor
vida sexual
gravidez
sangramentos
uso de anticoncepcional
```

Por isso, a arquitetura deve considerar privacidade desde o início.

---

## 27. Funcionalidades mínimas de privacidade

O sistema deve permitir:

```txt
consentimento explícito
registro do consentimento
revogação de consentimento
exportação de dados
exclusão de dados
política de privacidade clara
termos de uso
limitação de finalidade
logs sem conteúdo sensível quando possível
criptografia em repouso quando possível
criptografia em trânsito
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

# Parte 9 — Deploy e infraestrutura

## 29. Deploy inicial recomendado

### Site

```txt
Vercel
```

### Backend

Opções:

```txt
Render
Railway
Fly.io
DigitalOcean VPS
Azure App Service
```

### Banco

Opções:

```txt
Supabase Postgres
Neon
Railway Postgres
DigitalOcean Managed PostgreSQL
```

### Redis

Opções:

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

## 32. Deploy produção mais sério

```txt
Site: Vercel
Backend: Azure App Service, Azure Container Apps, AWS ECS ou DigitalOcean App Platform
Banco: PostgreSQL gerenciado
Redis gerenciado
Observabilidade: Sentry + OpenTelemetry
```

---

# Parte 10 — Observabilidade e qualidade

## 33. Logs

Para logs em C#:

```txt
Serilog
```

Usar logs estruturados, evitando registrar conteúdo sensível das mensagens.

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
conteúdo completo da mensagem
sintomas íntimos detalhados
informações sexuais
número completo de telefone sem mascaramento
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

## 35. Documentação da API

Usar:

```txt
Swagger / OpenAPI
```

ASP.NET Core possui integração fácil com Swagger.

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
cálculo de ciclo
cálculo de atraso
registro de início/fim de menstruação
alteração de intensidade
bloqueio de diagnóstico
rate limit
webhook do WhatsApp
webhook de pagamento
exclusão de dados
```

---

# Parte 11 — Fases de desenvolvimento

## 37. Fase 1 — Landing page

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
formulário de lista de espera
página de obrigado
política de privacidade inicial
termos iniciais
```

---

## 38. Fase 2 — Pré-cadastro e validação comercial

### Objetivo

Medir intenção real de uso e pagamento.

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
segmentação de leads
campanhas de e-mail/WhatsApp autorizadas
pesquisa de interesse
possível pré-venda
```

---

## 39. Fase 3 — Bot MVP

### Objetivo

Criar a primeira versão funcional da Luma pelo WhatsApp.

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
identificar usuária
registrar início da menstruação
registrar fim da menstruação
registrar intensidade
registrar sintomas
responder perguntas simples
calcular próxima menstruação
calcular atraso
limitar spam
registrar consentimento
```

---

## 40. Fase 4 — Painel/admin

### Objetivo

Gerenciar operação, suporte e visualização básica.

### Opções de stack

```txt
Next.js Admin
Blazor
ASP.NET Core MVC
```

### Recomendação

Para velocidade e consistência visual:

```txt
Next.js Admin
```

Para aplicar ainda mais C#:

```txt
Blazor
```

### Entregas

```txt
visualizar usuárias
visualizar status de assinatura
consultar logs não sensíveis
acompanhar mensagens processadas
reenviar mensagens com falha
bloquear/desbloquear usuárias
```

---

## 41. Fase 5 — Plataforma completa

### Objetivo

Transformar o MVP em produto real.

### Entregas

```txt
assinaturas recorrentes
painel da usuária
exportação de dados
exclusão de conta
lembretes configuráveis
modo gravidez opcional
relatórios de ciclo
melhorias de IA
métricas de uso
observabilidade avançada
```

---

# Parte 12 — Stack final recomendada

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

# Parte 13 — Decisão final sugerida

A recomendação final é:

```txt
Luma Site
Next.js + TypeScript + Tailwind CSS + Vercel

Luma Platform
ASP.NET Core Web API + PostgreSQL + Entity Framework Core + Redis + Hangfire + Docker

IA
Gemini como parser e humanizador, não como tomador de decisão médica

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

A IA pode ajudar a interpretar e humanizar, mas as decisões importantes devem estar no backend, em regras determinísticas, testáveis e auditáveis.

---

## 49. Resumo executivo

A Luma deve ser construída em duas frentes:

```txt
1. Um site simples, bonito e rápido para validar interesse.
2. Um backend robusto em C# para operar o bot de forma segura.
```

A escolha de **C# com ASP.NET Core** para o backend é tecnicamente adequada e estratégica, porque permite criar uma plataforma bem estruturada, com regras de negócio, jobs, webhooks, integrações e privacidade desde o início.

O site deve continuar simples, usando tecnologias de frontend modernas e rápidas, enquanto o backend concentra a complexidade real do produto.

