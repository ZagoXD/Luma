# Proposta e Status da Migração para OpenAI

Última atualização: 03/05/2026.

## Decisão

A Luma passou a usar OpenAI como motor principal de IA em desenvolvimento e produção. A ideia é manter comportamento consistente entre ambientes, reduzir latência imprevisível local e evitar travamentos que aconteceram durante testes com Ollama.

## Status Atual

Implementado:

- OpenAI configurada na API.
- Ollama removido do projeto e do Docker.
- Agente de tools com resposta estruturada.
- Geração de resposta final humanizada pela IA.
- Backend autoritativo validando todas as ações.
- RAG interno simples via `LumaKnowledgeBase`.
- OpenAI Images para imagem educativa do bebê.
- Bloqueios autoritativos por plano antes de executar áudio, imagem ou notificações.

## Arquitetura Conversacional

```txt
Mensagem da usuária
  -> contexto da Luma
  -> estado atual da usuária
  -> tools disponíveis
  -> base RAG relevante
  -> OpenAI sugere uma ação
  -> backend valida
  -> backend verifica plano e privacidade
  -> backend executa leitura/escrita autorizada
  -> OpenAI escreve resposta final quando permitido
  -> WhatsApp
```

## Papel da IA

A IA pode:

- interpretar intenção;
- extrair dados naturais;
- escolher uma tool;
- adaptar a resposta ao contexto;
- consultar conhecimento seguro;
- responder de forma mais humana.

A IA não pode:

- gravar direto no banco;
- ignorar consentimento;
- confirmar diagnóstico;
- confirmar gravidez;
- dizer se sangramento é normal;
- burlar validações de plano;
- conversar em grupo;
- executar ações fora das tools permitidas.
- descriptografar dados por conta própria.

## Tools Atuais

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
- `get_baby_development`
- `generate_baby_size_image`
- `get_cycle_calendar`
- `search_luma_knowledge_base`
- `medical_guardrail`
- `out_of_scope`

## Variáveis

```env
OpenAI__ApiKey=
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__Model=gpt-5.4-mini
OpenAI__TimeoutSeconds=12
OpenAI__MaxOutputTokens=700
OpenAI__ReasoningEffort=none
OpenAI__ImageModel=gpt-image-1
```

## Próximas Melhorias

- Evoluir `LumaKnowledgeBase` para RAG persistido com embeddings.
- Criar avaliação automática de qualidade das respostas.
- Adicionar observabilidade de custo/latência por mensagem.
- Criar suíte de testes de conversas completas com snapshots aprovados.
- Refinar prompts de identidade e segurança.

## Por Que Não Usar n8n Agora

O n8n não foi adotado na V1 porque a orquestração está fortemente acoplada a regras de saúde, assinatura, privacidade e persistência. Manter a orquestração na API reduz latência, simplifica deploy e mantém o backend como autoridade única.
