# Especificação do Bot Luma para WhatsApp

Última atualização: 30/04/2026.

## Objetivo

A Luma é uma assistente de WhatsApp para apoio ao acompanhamento de ciclo menstrual, sintomas, humor, relação sexual registrada e gravidez. Ela conversa de forma acolhedora, organiza registros e responde perguntas dentro do escopo permitido, sempre deixando claro que não substitui orientação médica e não realiza diagnóstico.

## Princípios do Produto

- Conversa natural em PT-BR, com tom humano, cuidadoso e acolhedor.
- Backend autoritativo: a IA interpreta intenções, mas não grava dados diretamente.
- Dados sensíveis tratados com consentimento, mínimo necessário e sem salvar corpo das mensagens por padrão.
- Guardrails fixos para temas médicos, LGPD, privacidade, menores de idade e conversas fora de escopo.
- Usuárias só podem conversar pelo WhatsApp se tiverem plano ativo ou cancelado ainda dentro do período pago.

## Fluxo Atual de Mensagem

```txt
WhatsApp/Twilio
  -> Webhook da API
  -> Bloqueio defensivo de grupos
  -> Deduplicação por MessageSid
  -> Rate limit/cooldown por telefone
  -> Lock de processamento por telefone
  -> Validação de assinatura
  -> ConversationService
  -> Agente OpenAI escolhe uma tool
  -> Backend valida e executa
  -> OpenAI gera resposta acolhedora quando permitido
  -> TwiML para Twilio
```

## Cadastro Inicial

O cadastro inicial está implementado e cobre:

1. Consentimento para armazenamento de dados relacionados a ciclo, sintomas e saúde menstrual.
2. Nome de exibição.
3. Confirmação de maioridade.
4. Primeiro dia da última menstruação.
5. Duração média do ciclo.
6. Duração média da menstruação.
7. Método contraceptivo.

Durante o cadastro, se a usuária mandar uma intenção fora de ordem, como "menstruei hoje" enquanto a Luma espera o nome, o backend pode salvar uma intenção pendente e retomar depois do cadastro.

## Ciclo Menstrual

Implementado:

- Registro de início de menstruação.
- Registro de término de menstruação.
- Registro de fluxo: leve, médio, intenso ou não informado.
- Registro de sintomas, como cólica, dor, enjoo e outros relatos.
- Registro de humor.
- Atualização da duração média da menstruação.
- Cálculo estimado da próxima menstruação.
- Cálculo estimado de atraso menstrual.
- Consulta da última menstruação registrada.
- Consulta do último sintoma registrado.

Regras:

- Datas relativas e naturais são interpretadas pela IA e validadas pelo backend.
- Exemplos: "ontem", "anteontem", "há 5 dias", "dia 10", "dia 30 do mês passado".
- A Luma sempre comunica que previsões são estimativas.

## Relação Sexual

Implementado:

- Registro de relação sexual/intimidade informada pela usuária.
- Interpretação por IA para variações naturais de linguagem.
- Consulta da última relação sexual registrada.
- Registro com proteção informada quando a usuária mencionar.

Guardrail:

- A Luma não calcula "risco seguro" de gravidez, não confirma gravidez e não substitui orientação médica.

## Gravidez

Implementado:

- Início de modo gravidez quando a usuária informa teste positivo ou gravidez.
- Registro de referência da gravidez:
  - data da última menstruação;
  - semanas de gestação;
  - data provável do parto;
  - "ainda não sei".
- Estimativa de semanas e data provável do parto quando houver dados suficientes.
- Registro de sangramento na gravidez com orientação segura.
- Registro de sintomas de gravidez.
- Registro de consulta pré-natal.
- Registro de ultrassom.

Guardrails:

- A Luma não confirma se a usuária está grávida.
- A Luma não diz se sangramento é normal.
- A Luma orienta buscar médico/obstetra em sinais de alerta.

## Inteligência Conversacional

Estado atual:

- A Luma usa OpenAI como motor principal de IA.
- Ollama foi removido do projeto.
- O backend expõe tools internas em estilo agente.
- A IA escolhe uma tool e o backend valida a execução.
- RAG interno simples é usado com a base `LumaKnowledgeBase`.

Tools principais:

- `complete_onboarding_step`
- `save_pending_intent`
- `record_period_start`
- `record_period_end`
- `record_flow_update`
- `record_symptom`
- `record_mood`
- `record_sexual_activity`
- `start_pregnancy_mode`
- `record_pregnancy_bleeding`
- `record_pregnancy_symptom`
- `record_prenatal_appointment`
- `record_ultrasound`
- `calculate_next_period`
- `calculate_delay`
- `get_last_period`
- `get_last_symptom`
- `get_last_sexual_activity`
- `get_notification_preferences`
- `update_notification_preferences`
- `disable_notification_preferences`
- `search_luma_knowledge_base`
- `medical_guardrail`
- `out_of_scope`

## Assinaturas

Implementado:

- Pré-cadastro web com autenticação JWT/cookie.
- Cadastro com e-mail, CPF, nome, senha e celular.
- Validação e normalização de CPF e celular.
- Checkout com Stripe Elements.
- Planos:
  - Luma Básico: R$ 5,90/mês.
  - Luma Essencial: R$ 9,90/mês.
- Webhooks Stripe para sincronizar assinatura.
- Cancelamento no fim do período.
- Retomada de assinatura.
- Troca de plano.
- Troca de cartão.
- Perfil web com dados de conta, plano, dados menstruais e número da Luma.

## Notificações do Plano Essencial

Implementado no backend e na web:

- Preferências de notificação por usuária.
- Worker de notificações.
- Registro de entregas.
- Locks Redis para evitar duplicidade.
- Painel no perfil web para ativar/desativar lembretes.
- Tools para IA consultar, ativar, alterar ou desativar lembretes.

Tipos:

- previsão de menstruação amanhã;
- previsão de menstruação hoje;
- anticoncepcional diário para pílula;
- check-in de sintomas/humor.

Status atual:

- Worker deve permanecer desativado até os templates Twilio/Meta serem criados e aprovados.
- Variável recomendada por enquanto: `Notifications__WorkerEnabled=false`.

## Segurança e Limites

Implementado:

- Usuária menor de 18 anos é bloqueada no WhatsApp.
- Sem plano ativo, a Luma não responde ao fluxo normal.
- Bloqueio defensivo para conversas em grupo.
- Rate limit com Redis e fallback em memória.
- Deduplicação de webhooks.
- Corpo de mensagens não é persistido por padrão.
- Guardrails médicos fixos.

## Fora de Escopo

A Luma deve recusar ou redirecionar quando a usuária pedir:

- diagnóstico;
- confirmação de gravidez;
- interpretação de sangramento como normal ou anormal;
- prescrição ou tratamento;
- probabilidade médica de gravidez;
- assuntos fora de ciclo menstrual, gravidez, saúde menstrual, privacidade ou uso da Luma.
