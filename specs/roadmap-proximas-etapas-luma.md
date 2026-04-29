# Luma - Roadmap para V1.0.0

Este documento organiza o estado atual do projeto Luma e o que ainda falta para chegar na primeira versão de produção: `v1.0.0`.

Ele deve ser lido junto com:

- `especificação-bot-ciclo-menstrual-whatsapp.md`
- `especificação-stacks-luma.md`

A regra arquitetural principal continua sendo:

> O backend decide. A IA entende, conversa e escreve.

Na prática, a Luma deve parecer uma assistente inteligente, mas a fonte de verdade continua sendo o backend: regras de negócio, segurança, LGPD, gravação no banco, cálculos e guardrails médicos.

---

# Estado atual do projeto

## Status geral

Status atual: **V1 transacional implementada; falta camada inteligente RAG/MCP para fechar a V1.0.0.**

A Luma já possui um núcleo funcional capaz de:

- receber mensagens pelo WhatsApp via Twilio Sandbox;
- rodar localmente via Docker Compose;
- persistir dados em PostgreSQL;
- usar OpenAI API;
- cadastrar usuárias;
- registrar ciclo menstrual;
- registrar sintomas, fluxo, humor e relação sexual;
- consultar histórico básico;
- registrar gravidez e eventos de gravidez;
- aplicar guardrails médicos principais;
- responder com segurança quando não deve diagnosticar;
- rodar testes automatizados para os fluxos principais.

O que ainda falta para `v1.0.0` não é criar mais regras fixas. O próximo passo é transformar esse núcleo em uma assistente mais viva, capaz de entender contexto, lidar com mensagens fora da ordem esperada e usar ferramentas controladas pelo backend.

---

# Etapas da V1

## Etapa 1 - App inicial e cadastro via WhatsApp

Status: **concluída**.

Entregas:

- Backend em ASP.NET Core Web API.
- Docker Compose com API e PostgreSQL.
- Integração inicial com Twilio Sandbox.
- Webhook de WhatsApp.
- Endpoint local de desenvolvimento.
- Cadastro inicial por conversa:
  - consentimento;
  - nome;
  - confirmação de maioridade;
  - última menstruação;
  - duração média do ciclo;
  - duração média da menstruação.
- Persistência em PostgreSQL.
- Event log inicial.
- Interpretação de datas naturais.
- Testes automatizados.

---

## Etapa 1.1 - Métodos contraceptivos no onboarding

Status: **concluída**.

Entregas:

- Pergunta opcional sobre método contraceptivo.
- Suporte a respostas como:
  - pilula;
  - injecao;
  - DIU hormonal;
  - DIU de cobre;
  - implante;
  - camisinha;
  - não uso;
  - prefiro não informar.
- Registro em preferências da usuária.
- Guardrails para não recomendar método contraceptivo e não afirmar "período seguro".
- Testes automatizados.

---

## Etapa 2 - Fluxo menstrual completo

Status: **concluída**.

Entregas:

- Registro de início da menstruação.
- Registro de fim da menstruação.
- Registro e atualização de fluxo.
- Registro de sintomas.
- Registro de humor e bem-estar.
- Registro de relação sexual.
- Consulta de:
  - próxima menstruação;
  - atraso;
  - última menstruação;
  - último sintoma;
  - última relação sexual.
- Cálculos básicos de ciclo e atraso.
- Guardrails para evitar diagnóstico, gravidez afirmada, período seguro e orientações médicas indevidas.
- Testes automatizados.

---

## Etapa 3 - Gravidez

Status: **concluída em nível MVP**.

Entregas:

- Entrada no modo gravidez.
- Registro de gravidez ativa.
- Registro por:
  - teste positivo;
  - semanas de gravidez;
  - data da última menstruação;
  - data provavel do parto.
- Calculo estimado de idade gestacional.
- Calculo estimado de data provavel do parto.
- Eventos:
  - `pregnancy_positive`;
  - `pregnancy_bleeding`;
  - `pregnancy_symptom`;
  - `prenatal_appointment`;
  - `ultrasound`;
  - `pregnancy_note`.
- Guardrail fixo para sangramento na gravidez.
- Respostas seguras para sintomas preocupantes.
- Testes automatizados.

Observacao:

Esta etapa esta pronta para MVP, mas a experiência conversacional ainda será melhorada pela Etapa 4. A IA não deve assumir decisão médica; deve apenas organizar e explicar com base nas regras do backend.

---

## Etapa 4 - Luma inteligente com RAG, MCP/tools e orquestração de IA

Status: **próxima etapa; pendente para fechar V1.0.0**.

Objetivo:

Transformar a Luma de um fluxo transacional com respostas fixas em uma assistente conversacional capaz de:

- entender mensagens fora da ordem esperada;
- manter contexto da conversa;
- guardar intenções pendentes;
- consultar uma base de conhecimento segura;
- chamar ferramentas de leitura/escrita controladas pelo backend;
- humanizar respostas;
- explicar quem ela e e o que pode fazer;
- recusar temas fora de escopo;
- manter LGPD, segurança e guardrails médicos.

Essa etapa não substitui o backend. Ela cria uma camada inteligente acima dele.

Fluxo alvo:

```txt
Mensagem da usuária
  ->
Orquestrador da Luma com prompt de identidade, estado da usuária e ferramentas disponíveis
  ->
IA interpreta intenção e contexto
  ->
Backend valida regras, LGPD e guardrails
  ->
Backend executa leitura/escrita autorizada
  ->
RAG recupera conteúdo seguro quando necessário
  ->
IA escreve resposta final acolhedora
  ->
WhatsApp
```

---

# Etapa 4 em detalhes

## 4.1 Prompt de identidade da Luma

Criar um prompt versionado definindo:

- quem e a Luma;
- tom de voz;
- público-alvo;
- o que ela pode fazer;
- o que ela não pode fazer;
- regras de privacidade;
- regras de LGPD;
- limites médicos;
- quando orientar procurar médico;
- como lidar com incerteza;
- como lidar com mensagens fora de ordem;
- como pedir confirmação antes de salvar eventos sensíveis.

Exemplo de identidade:

```txt
Você e a Luma, uma assistente de ciclo menstrual e gravidez pelo WhatsApp.
Seu papel é ajudar a usuária a registrar e consultar informações pessoais sobre ciclo, sintomas, fluxo, humor, relação sexual e gravidez.
Você não faz diagnósticos, não confirma gravidez, não diz que sangramentos são normais e não substitui orientação médica.
Quando houver risco, oriente procurar profissional de saúde.
```

## 4.2 Estado conversacional

O orquestrador deve receber o estado atual da usuária:

```txt
onboarding_step
pending_action
pending_intent
user_profile
cycle_summary
pregnancy_status
last_events_summary
consent_status
```

Isso resolve casos como:

```txt
Etapa atual: aguardando nome
Usuária: menstruei hoje
```

Resposta esperada:

```txt
Entendi. Já vi que você quer registrar que sua menstruação começou hoje.
Antes disso, preciso terminar seu cadastro rapidinho para salvar tudo certinho e com segurança.
Como devo te chamar?
```

Depois do cadastro:

```txt
Você tinha me contado que menstruou hoje. Quer que eu registre isso agora?
```

Se confirmar:

```txt
Backend registra period_start.
```

## 4.3 Memória de intenção pendente

Adicionar suporte conceitual a intenções pendentes.

Exemplos:

```txt
pending_intent: period_start
pending_date: 2026-04-28
pending_confirmation_required: true
pending_reason: user_sent_event_during_onboarding
```

Eventos que podem virar intenção pendente:

- início da menstruação;
- fim da menstruação;
- fluxo;
- sintoma;
- humor;
- relação sexual;
- gravidez;
- sangramento na gravidez;
- consulta pré-natal;
- ultrassom.

Regra:

> Nenhuma intenção sensível enviada fora da etapa atual deve ser descartada silenciosamente. A Luma deve reconhecer, explicar o que falta e retomar depois com confirmação.

## 4.4 RAG

Criar uma base de conhecimento controlada para respostas educativas e institucionais.

Conteudos iniciais:

- Quem e a Luma.
- Limites da Luma.
- LGPD e privacidade.
- Consentimento.
- Ciclo menstrual.
- Sintomas menstruais.
- Atraso menstrual.
- Fluxo menstrual.
- Relação sexual e histórico.
- Gravidez.
- Sangramento na gravidez.
- Quando procurar médico.
- O que a Luma não pode responder.

Formato inicial recomendado:

```txt
knowledge/
  luma-identidade.md
  lgpd-privacidade.md
  ciclo-menstrual.md
  sintomas.md
  atraso-menstrual.md
  relação-sexual.md
  gravidez.md
  sangramento-gravidez.md
  guardrails-médicos.md
```

Para a V1.0.0, a base pode comecar simples, usando Markdown versionado no repositorio. Depois pode evoluir para embeddings em PostgreSQL/pgvector ou outro vector store.

## 4.5 MCP ou tools internas

O conceito de MCP deve ser usado como arquitetura de ferramentas, mesmo que a primeira implementacao seja interna no backend.

Ferramentas sugeridas:

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

Regras das tools:

- A IA nunca escreve direto no banco.
- A IA solicita uma tool.
- O backend valida permissão, estado, consentimento, assinatura futura e guardrails.
- O backend executa.
- O backend devolve resultado estruturado.
- A IA humaniza a resposta final.

## 4.6 Orquestrador da conversa

Criar um servico responsável por coordenar IA, RAG e tools.

Nome sugerido:

```txt
LumaConversationOrchestrator
```

Responsabilidades:

- montar contexto da conversa;
- chamar o modelo;
- interpretar tool calls ou JSON estruturado;
- consultar RAG;
- chamar tools autorizadas;
- aplicar guardrails;
- montar resposta final;
- registrar logs sem conteúdo sensível.

## 4.7 Humanização dinâmica

Substituir gradualmente respostas fixas por respostas geradas pela IA.

O backend deve continuar retornando um resultado estruturado:

```json
{
  "action": "period_start_registered",
  "date": "2026-04-28",
  "requires_médical_guardrail": false,
  "next_question": "flow_intensity"
}
```

A IA transforma isso em uma mensagem acolhedora:

```txt
Pronto, registrei que sua menstruação começou hoje. Obrigada por me contar.
Como esta o fluxo agora: leve, medio ou intenso?
```

## 4.8 Guardrails fixos

Mesmo com IA, essas respostas devem continuar controladas pelo backend:

- diagnóstico médico;
- confirmar ou descartar gravidez;
- dizer se sangramento e normal;
- dizer que não precisa procurar médico;
- orientar relação sem proteção;
- afirmar período seguro;
- lidar com menor de idade;
- consentimento LGPD;
- exclusão/exportação de dados;
- mensagens de crise ou risco grave.

## 4.9 Testes obrigatorios da Etapa 4

Testes mínimos:

- usuário manda evento menstrual durante onboarding;
- usuário manda gravidez durante onboarding;
- usuário manda relação sexual durante onboarding;
- usuário manda sintoma durante onboarding;
- intenção pendente e salva e confirmada após cadastro;
- intenção pendente é descartada se usuária negar;
- IA pede tool valida;
- IA tenta pedir tool proibida;
- RAG responde pergunta educativa sem diagnosticar;
- pergunta fora de escopo e recusada;
- pergunta sobre LGPD usa resposta controlada;
- sangramento na gravidez usa guardrail fixo;
- falha da OpenAI API retorna fallback seguro;
- logs não salvam conteúdo sensível por padrão.

---

# Definicao de pronto da V1.0.0

A Luma estará pronta para `v1.0.0` quando:

- Etapas 1, 1.1, 2 e 3 estiverem implementadas e testadas.
- Etapa 4 estiver implementada com orquestração inteligente.
- A Luma conseguir lidar com mensagens fora de ordem sem perder a intenção da usuária.
- Existir memória de intenção pendente.
- Existir confirmação antes de gravar evento sensível fora do fluxo esperado.
- Existir RAG inicial com conteúdo versionado.
- Existirem tools internas ou MCP para leitura/escrita controlada.
- O backend continuar autoritativo.
- Guardrails médicos e LGPD não dependerem apenas da IA.
- Respostas fixas forem reduzidas aos casos de segurança, LGPD e falha.
- OpenAI API estiver integrada ao fluxo inteligente.
- Houver fallback seguro se a OpenAI API estiver indisponível.
- Testes automatizados cobrirem os fluxos principais.
- Docker Compose subir API e PostgreSQL.
- Twilio Sandbox ou provedor equivalente estiver validado.
- README de desenvolvimento estiver atualizado.

---

# Depois da V1.0.0: V2 SaaS

Depois da primeira versão de produção, o foco passa a ser SaaS.

## Etapa 5 - Website, cadastro e assinaturas

Status: **pós-V1.0.0**.

Escopo:

- Landing page em Next.js.
- Cadastro da usuária.
- Login.
- Política de privacidade.
- Termos de uso.
- Checkout de assinatura.
- Integração com provedor de pagamento.
- Webhook de pagamento.
- Tabela de assinaturas.

Provedores possíveis:

```txt
Asaas
Mercado Pago
Stripe Billing
Pagar.me
```

## Etapa 5.1 - Validação de assinatura no bot

Status: **pós-V1.0.0**.

Escopo:

- Identificar numero de WhatsApp.
- Verificar conta.
- Verificar assinatura ativa ou trial.
- Bloquear uso sem assinatura.
- Orientar regularizacao pelo site.

## Etapa 6 - Anti-spam, grupos e hardening

Status: **pós-V1.0.0 ou preparação final de produção, dependendo do risco do piloto**.

Escopo:

- Rate limit por numero.
- Timeout temporario.
- Deteccao de spam.
- Deteccao de grupos.
- Validação antes de chamar IA.
- Observabilidade.
- Logs estruturados.
- Sentry/Application Insights.
- Exportação e exclusão de dados.

---

# Ordem recomendada a partir de agora

```txt
1. Projetar prompt de identidade da Luma
2. Criar base RAG inicial em Markdown
3. Definir contrato das tools internas/MCP
4. Criar memória de intenção pendente
5. Criar LumaConversationOrchestrator
6. Integrar OpenAI API ao orquestrador
7. Substituir respostas fixas por humanização dinâmica
8. Manter guardrails fixos no backend
9. Testar fluxos fora de ordem
10. Revalidar fluxo completo via WhatsApp
11. Atualizar README e preparar tag v1.0.0
```

---

# Resumo executivo

A Luma já tem o motor funcional.

O que falta para a versão `1.0.0` é a camada de inteligência conversacional:

- RAG para conhecimento seguro.
- Tools/MCP para a IA operar o backend.
- Memória de intenções pendentes.
- Orquestração de IA com OpenAI API.
- Humanização dinâmica.
- Guardrails fixos para segurança e LGPD.

Essa e a etapa que vai transformar a Luma de um bot com fluxos em uma assistente de verdade.
