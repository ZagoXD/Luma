# Proposta de migração da Luma para OpenAI API

## Contexto

Durante os testes pelo WhatsApp/Twilio, o fluxo com Ollama local funcionou corretamente no backend, mas apresentou latência alta demais para o webhook.

Exemplo observado em 28/04/2026:

- Usuário enviou: `Aceito sim`
- Backend chamou `ILumaToolAgent` usando Ollama local
- Ollama respondeu em apróximadamente 16,5 segundos
- Backend gravou o consentimento e avancou para `awaiting_display_name`
- Twilio não exibiu a resposta para a usuária, provavelmente por timeout do webhook

Ou seja: o sistema processou, mas respondeu tarde demais para uma experiência confiavel no WhatsApp.

## Diagnóstico

O problema principal não é apenas "inteligência" do modelo. É a combinação de:

- Modelo local pequeno (`llama3.2`) com latência variável.
- Execucao local via Docker/CPU, sem garantia de tempo de resposta.
- Webhook do Twilio esperando resposta sincrona.
- Fluxos onde a IA e chamada para interpretar mensagens simples como consentimento.
- Necessidade de tool calling/JSON confiavel para a Luma agir como agente.

O Ollama continua útil para desenvolvimento local, privacidade e testes sem custo por token. Mas, para uma V1 de produção com WhatsApp, ele não oferece previsibilidade suficiente neste momento.

## Recomendação

Migrar a camada de IA principal da Luma para a OpenAI API, mantendo Ollama como fallback/local-dev.

Modelo recomendado para V1:

- `gpt-5.4-mini` como modelo principal de produção, priorizando latência/custo.
- `gpt-5.5` como opcional para avaliações, prompts complexos, testes de qualidade e casos que exigirem mais raciocínio.
- Evitar modelos "pro" no webhook sincrono, porque podem ser mais lentos e caros.

Essa recomendação segue a orientação atual da documentação da OpenAI: a página de modelos indica `gpt-5.5` para raciocínio complexo e `gpt-5.4-mini`/`gpt-5.4-nano` quando o objetivo é menor latência e menor custo.

Referência: https://developers.openai.com/api/docs/models

## Arquitetura proposta

Fluxo de mensagem:

```text
WhatsApp/Twilio
-> API Luma
-> Guardrails fixos mínimos
-> OpenAI Responses API com tools/structured output
-> Backend valida a ação
-> Backend executa leitura/escrita autorizada
-> OpenAI escreve resposta final quando apropriado
-> Twilio
```

O backend continua sendo autoritativo. A IA nunca grava direto no banco.

## O que muda

Criar uma abstracao de provedor de IA:

```text
ILumaAiProvider
  - OllamaLumaAiProvider
  - OpenAiLumaAiProvider
```

Configurar via ambiente:

```text
LUMA_AI_PROVIDER=openai
OPENAI_API_KEY=...
OPENAI_MODEL=gpt-5.4-mini
OPENAI_REASONING_EFFORT=none
```

Manter:

```text
LUMA_AI_PROVIDER=ollama
OLLAMA_BASE_URL=http://ollama:11434
OLLAMA_MODEL=llama3.2
```

## Tools previstas

A Luma deve usar function/tool calling para solicitar ações, sempre validadas pelo backend:

- `get_user_profile`
- `get_onboarding_state`
- `save_pending_intent`
- `complete_onboarding_step`
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
- `search_luma_knowledge_base`

A OpenAI API possui suporte oficial a function calling/tool calling. A documentação descreve essa capacidade como a forma de conectar modelos a dados e ações fornecidas pela aplicação.

Referência: https://developers.openai.com/api/docs/guides/function-calling

## Structured Outputs

Para evitar respostas inválidas do tipo JSON quebrado, tool inexistente ou enum inventado, a migração deve usar Structured Outputs sempre que a Luma precisar devolver uma decisão estruturada.

Exemplo de saída esperada da IA:

```json
{
  "intent": "complete_onboarding_step",
  "confidence": 0.94,
  "tool": {
    "name": "complete_onboarding_step",
    "arguments": {
      "consent_accepted": true
    }
  },
  "final_reply_style": "warm_short"
}
```

A documentação oficial indica que Structured Outputs garante aderência a um JSON Schema definido pela aplicação, reduzindo erros de formato e valores inesperados.

Referência: https://developers.openai.com/api/docs/guides/structured-outputs

## Guardrails que continuam fixos

Mesmo com OpenAI, alguns pontos devem continuar no backend:

- Consentimento LGPD.
- Bloqueio para menores de 18 anos.
- Frases de emergência/risco médico.
- Não afirmar gravidez.
- Não diagnosticar sangramento, dor, aborto, infecção ou risco fetal.
- Não executar escrita no banco sem validação.
- Não aceitar saudação pura como consentimento.
- Timeouts e fallback.

Isso não é retrocesso para "mensagens chumbadas"; é segurança de produto. A IA interpreta e conversa, mas o backend protege o limite legal/médico.

## Resposta sobre o problema do "Olá" e "Aceito sim"

Migrar para OpenAI deve melhorar bastante:

- Menor latência média em comparação com Ollama local em CPU/Docker.
- Melhor interpretação de frases naturais como `Aceito sim`, `claro`, `pode seguir`, `com certeza`.
- Melhor tool calling e aderência ao schema.
- Menos necessidade de criar casos manuais para cada frase.

Mas a migração não deve significar "tudo passa pela IA sempre".

Exemplo:

- `Olá` na primeira mensagem pode ser respondido pelo backend imédiatamente com o texto de consentimento.
- `Aceito sim` pode ir para a IA ou para um classificador rápido, mas com timeout curto.
- Mensagens ambiguas, fora de ordem ou ricas em contexto devem ir para o agente.

Assim a Luma fica inteligente sem sacrificar a experiência do WhatsApp.

## Plano de implementacao

### Etapa A - Abstracao de provedor

- Criar `ILumaAiProvider`.
- Migrar chamadas atuais de Ollama para uma interface única.
- Adicionar configuracao por `.env`.
- Manter Ollama funcionando em desenvolvimento.

### Etapa B - OpenAI Responses API

- Implementar `OpenAiLumaAiProvider`.
- Usar `OPENAI_API_KEY`.
- Usar `OPENAI_MODEL=gpt-5.4-mini` como padrão.
- Configurar timeouts curtos para webhook.

### Etapa C - Tool calling real

- Converter `LumaToolAgent` para tools/function calling nativo.
- Remover dependências de JSON livre quando possível.
- Validar todos os argumentos no backend antes de executar.

### Etapa D - Structured Outputs

- Definir schema único para decisão da Luma.
- Criar testes para:
  - consentimento natural;
  - nome + idade na mesma frase;
  - menstruação fora de ordem;
  - relação sexual fora de ordem;
  - dúvida de gravidez;
  - pedido fora do escopo;
  - mensagem perigosa/médica.

### Etapa E - RAG controlado

- Manter a base RAG local/curada.
- Passar trechos relevantes no contexto do modelo.
- Nunca permitir que o modelo invente orientação médica fora da base e dos guardrails.

### Etapa F - Fallback e observabilidade

- Se a OpenAI falhar: fallback para resposta segura ou Ollama, dependendo do tipo de mensagem.
- Logar latência por chamada.
- Logar tool escolhida, sem armazenar conteúdo sensível além do necessário.
- Criar metricas:
  - tempo medio por mensagem;
  - taxa de timeout;
  - taxa de fallback;
  - tools mais usadas;
  - mensagens não entendidas.

## Riscos

- Custo por token.
- Dados trafegando para provedor externo, exigindo revisão de política de privacidade/LGPD.
- Dependência de rede e disponibilidade da API.
- Necessidade de configurar billing, limites e chave segura.

## Decisão recomendada

Para a V1 de produção/testes reais no WhatsApp, a recomendação é:

- OpenAI API como provedor principal.
- Ollama como fallback/local-dev.
- Backend autoritativo.
- Guardrails fixos mínimos.
- IA responsável por interpretar linguagem natural, chamar tools e humanizar respostas.

Essa combinação deve deixar a Luma mais inteligente e, principalmente, mais previsível para uma experiência real de WhatsApp.
