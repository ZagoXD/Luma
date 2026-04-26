# Luma - Roadmap das proximas etapas

Este documento organiza as proximas fases da Luma a partir das especificacoes:

- `especificacao-bot-ciclo-menstrual-whatsapp.md`
- `especificacao-stacks-luma.md`

A regra central continua sendo:

> O sistema decide. A IA escreve.

Ou seja: a IA pode ajudar a interpretar mensagens naturais e tornar respostas mais humanas, mas as decisoes importantes devem ficar no backend, com regras deterministicas, testaveis e auditaveis.

---

## Estado atual

### Etapa 1 - App inicial e cadastro via WhatsApp

Status: concluida.

Entregas ja implementadas:

- Backend inicial em ASP.NET Core Web API.
- Docker Compose com API, PostgreSQL e Ollama.
- Integracao inicial com Twilio Sandbox para WhatsApp.
- Webhook de mensagens do WhatsApp.
- Cadastro inicial da usuaria por conversa:
  - consentimento para tratamento de dados sensiveis;
  - nome de exibicao;
  - confirmacao de maioridade;
  - primeiro dia da ultima menstruacao;
  - duracao media do ciclo;
  - duracao media da menstruacao.
- Persistencia no PostgreSQL.
- Event log inicial para ciclo menstrual.
- Interpretacao de datas naturais como:
  - hoje;
  - ontem;
  - antes de ontem;
  - ha alguns dias;
  - dia 10;
  - dia 10 de abril;
  - dia 30 do mes passado.
- Integracao do Ollama via Docker usando `llama3.2`.
- Fallback quando a mensagem nao e compreendida.
- Testes automatizados para onboarding e interpretacao de datas.

Objetivo atingido:

> Subir o servidor local com Docker, mandar mensagem para o numero configurado no Twilio e receber resposta da Luma pelo WhatsApp ate completar o cadastro inicial.

---

# V1 - MVP funcional da Luma

Ao final das etapas 1.1, 2, 3 e 4, a Luma deve estar pronta para uma primeira versao real de testes com usuarias. A usuaria deve conseguir se cadastrar, registrar eventos de menstruacao, registrar eventos de gravidez quando aplicavel, consultar previsoes basicas e receber respostas seguras quando perguntar algo que a Luma nao deve afirmar.

## Principios obrigatorios da V1

- Desenvolvimento da API em TDD daqui em diante.
- Criar ou atualizar testes antes de implementar novas regras de negocio.
- Manter logs sem conteudo sensivel sempre que possivel.
- Nao salvar texto completo de mensagens sensiveis por padrao.
- Bloquear ou redirecionar respostas medicas, diagnosticas ou de alto risco.
- Usar linguagem de estimativa, nunca certeza, para previsoes.
- Manter o WhatsApp desacoplado por interface para permitir troca futura de provedor.
- Manter IA como parser/humanizador, nao como fonte de verdade medica.

---

## Etapa 1.1 - Metodos contraceptivos no onboarding

Objetivo:

Adicionar ao cadastro inicial a coleta opcional de informacoes sobre metodos contraceptivos, com linguagem acolhedora e sem julgamento.

Escopo funcional:

- Perguntar se a usuaria usa algum metodo contraceptivo.
- Aceitar respostas naturais como:
  - "tomo pilula";
  - "uso DIU";
  - "uso camisinha";
  - "injecao";
  - "implante";
  - "nao uso";
  - "prefiro nao informar".
- Registrar o tipo de metodo quando informado.
- Permitir que a usuaria pule a pergunta.
- Nao fazer recomendacoes contraceptivas.
- Nao afirmar periodo seguro.
- Nao incentivar janela fertil como metodo contraceptivo.

Dados sugeridos:

```txt
uses_contraceptive: true/false/unknown/prefer_not_say
contraceptive_methods: pill/injection/hormonal_iud/copper_iud/implant/condom/other/unknown
contraceptive_notes: opcional, evitar texto livre sensivel no MVP
```

Eventos possiveis:

```txt
contraceptive_taken
contraceptive_missed
contraceptive_changed
```

Respostas seguras:

```txt
Obrigada por me contar. Vou usar isso apenas para organizar seus registros, sem fazer diagnosticos ou dizer se ha risco ou nao.
```

Criterios de pronto:

- Testes cobrindo respostas diretas e naturais.
- Testes cobrindo "prefiro nao informar".
- Dados salvos em `user_preferences` ou tabela propria.
- Nenhuma resposta sugere conduta medica ou contraceptiva.

---

## Etapa 2 - API completa para ciclo menstrual

Objetivo:

Implementar a experiencia principal da Luma para menstruacao, sintomas, fluxo, humor, historico e previsoes.

### 2.1 Registro de inicio da menstruacao

Mensagens esperadas:

```txt
menstruei hoje
desceu ontem
comecei a menstruar dia 10
minha menstruacao veio faz 3 dias
```

Comportamento esperado:

- Criar ou atualizar ciclo atual.
- Criar evento `period_start`.
- Calcular previsao da proxima menstruacao.
- Perguntar intensidade do fluxo quando ainda nao informada.
- Tratar conflito caso ja exista ciclo aberto.

Testes obrigatorios:

- Criacao de ciclo novo.
- Atualizacao quando ja ha ciclo aberto.
- Datas relativas e datas absolutas.
- Mensagem ambigua com fallback seguro.

### 2.2 Registro de fim da menstruacao

Mensagens esperadas:

```txt
acabou hoje
parou ontem
minha menstruacao terminou dia 15
```

Comportamento esperado:

- Encerrar ciclo aberto.
- Criar evento `period_end`.
- Calcular duracao real da menstruacao.
- Atualizar medias da usuaria quando fizer sentido.
- Responder com estimativa da proxima menstruacao.

Cuidados:

- Se nao houver ciclo aberto, pedir confirmacao ou perguntar a data de inicio.
- Se a data de fim for anterior ao inicio, pedir correcao.

### 2.3 Intensidade do fluxo

Valores:

```txt
light
medium
intense
unknown
```

Mensagens esperadas:

```txt
fluxo leve
hoje esta medio
veio muito forte
esta bem intenso
```

Comportamento esperado:

- Criar evento `flow_update`.
- Permitir uma intensidade por dia.
- Se a usuaria alterar no mesmo dia, confirmar ou sobrescrever conforme regra definida.

### 2.4 Sintomas

Sintomas iniciais:

```txt
colica
dor de cabeca
nausea
sensibilidade nos seios
inchaco
acne
dor lombar
sangramento fora do periodo
corrimento
cansaco
insonia
desejo alimentar
```

Intensidades:

```txt
leve
moderado
forte
```

Comportamento esperado:

- Criar evento `symptom`.
- Permitir multiplos sintomas no mesmo dia.
- Interpretar intensidade quando possivel.
- Quando houver sintoma preocupante, usar resposta segura.

Exemplo de guardrail:

```txt
Nao consigo avaliar se isso e normal por aqui. Posso registrar para seu historico, mas se houver dor forte, febre, sangramento intenso, tontura ou mal-estar importante, procure orientacao medica.
```

### 2.5 Humor e bem-estar

Humores iniciais:

```txt
irritada
triste
ansiosa
bem
sensivel
cansada
com energia
```

Comportamento esperado:

- Criar evento `mood`.
- Permitir historico por ciclo.
- Evitar diagnosticos como "voce tem TPM forte".
- Usar frases como "parece haver um padrao nos seus registros".

### 2.6 Relacao sexual

Comportamento esperado:

- Registro opcional e sensivel.
- Criar evento `sexual_activity`.
- Opcionalmente registrar se houve protecao, com `yes/no/unknown/prefer_not_say`.
- Nao usar isso para afirmar gravidez.
- Nao dizer que existe "periodo seguro".

Resposta segura:

```txt
Registrei para o seu historico. Eu nao uso esse dado para confirmar gravidez ou fazer diagnosticos.
```

### 2.7 Consultas e calculos

Perguntas esperadas:

```txt
quando e minha proxima menstruacao?
estou atrasada?
quando foi minha ultima menstruacao?
quantos dias durou meu ultimo ciclo?
qual foi meu ultimo sintoma registrado?
```

Calculos:

- Proxima menstruacao prevista.
- Dias de atraso.
- Duracao media do ciclo.
- Duracao media da menstruacao.
- Variacao dos ciclos.
- Sintomas mais frequentes.
- Intensidade media do fluxo.

Cuidados:

- Sempre usar linguagem estimada.
- Nunca prometer data exata.
- Se dados forem insuficientes, explicar que a previsao melhora com mais registros.

### 2.8 Respostas bloqueadas

A Luma nao deve responder afirmando:

```txt
voce esta gravida
voce nao esta gravida
esse sangramento e normal
voce tem infeccao
voce tem endometriose
pode ter relacao sem protecao
voce esta no periodo seguro
nao precisa procurar medico
```

Criterios de pronto da Etapa 2:

- Todos os eventos principais de ciclo implementados.
- Testes cobrindo intents, entidades, datas e conflitos.
- Testes cobrindo guardrails medicos.
- Historico basico consultavel por mensagem.
- Calculos principais implementados e testados.
- Fallback seguro para mensagens nao compreendidas.
- API continua funcionando via Twilio Sandbox e endpoint local de desenvolvimento.

---

## Etapa 3 - API completa para gravidez

Objetivo:

Implementar o modo gravidez com mensagens acolhedoras, acompanhamento basico e guardrails fortes.

### 3.1 Entrada no modo gravidez

Mensagens esperadas:

```txt
descobri que estou gravida
meu teste deu positivo
estou gravida de 8 semanas
```

Comportamento esperado:

- Criar registro de gravidez.
- Perguntar referencia para calculo:
  - data da ultima menstruacao;
  - semanas de gravidez;
  - data provavel do parto;
  - ainda nao sei.
- Calcular idade gestacional estimada quando houver dados suficientes.
- Calcular data provavel do parto quando possivel.

Resposta acolhedora:

```txt
Obrigada por me contar. Posso te ajudar a organizar essas informacoes por aqui, sempre como apoio aos seus registros e sem substituir seu pre-natal.
```

### 3.2 Eventos de gravidez

Eventos iniciais:

```txt
pregnancy_positive
pregnancy_bleeding
pregnancy_symptom
prenatal_appointment
ultrasound
pregnancy_note
```

Sintomas:

```txt
nausea
cansaco
sono
dor
colica
inchaço
azia
tontura
sangramento
```

### 3.3 Sangramento na gravidez

Esse caso deve ter guardrail fixo.

Resposta segura:

```txt
Registrei o sangramento para seu historico.

Sangramentos na gravidez podem ter varias causas, algumas simples e outras que precisam de avaliacao. Como voce esta gravida, e mais seguro entrar em contato com seu medico ou obstetra, principalmente se o sangramento for intenso, vier com dor forte, tontura, febre ou mal-estar.
```

A IA nao deve decidir se o sangramento e normal.

### 3.4 Perguntas de gravidez

Perguntas esperadas:

```txt
de quantas semanas estou?
qual minha data provavel do parto?
quando foi minha ultima consulta?
posso registrar meu ultrassom?
```

Comportamento esperado:

- Responder apenas com base nos dados registrados.
- Indicar que sao estimativas.
- Recomendar acompanhamento medico/pre-natal quando apropriado.

Criterios de pronto da Etapa 3:

- Modo gravidez ativo por usuaria.
- Registro de gravidez persistido.
- Eventos de gravidez implementados.
- Calculo de idade gestacional e data provavel do parto testados.
- Guardrails fixos para sangramento, dor intensa, febre, tontura e duvidas diagnosticas.
- Conversa acolhedora sem prometer orientacao medica.

---

## Etapa 4 - RAG, MCP e respostas mais humanizadas

Objetivo:

Evoluir a camada de IA para que a Luma converse melhor, mantendo seguranca, rastreabilidade e controle do backend.

Recomendacao de ordem:

> Esta etapa deve vir depois da Etapa 2 e da Etapa 3, ou em paralelo apenas para melhorar parsing/humanizacao. Antes disso, o dominio ainda precisa estar bem fechado e testado.

### 4.1 Separar servicos de IA

Interfaces sugeridas:

```txt
IMessageIntentParser
IResponseHumanizer
ISafetyGuardrailService
IKnowledgeRetrievalService
```

Responsabilidades:

- Parser: transformar mensagem natural em JSON estruturado.
- Humanizer: transformar resposta base em texto acolhedor.
- Guardrail: bloquear respostas perigosas.
- Retrieval: buscar conteudo aprovado quando houver base de conhecimento.

### 4.2 RAG

Usar RAG para respostas gerais e educativas, com base de conhecimento revisada.

Conteudos possiveis:

- Explicacoes sobre ciclo menstrual.
- Como interpretar registros pessoais.
- Limites da Luma.
- Avisos sobre quando procurar medico.
- Politica de privacidade resumida.
- Termos de uso resumidos.

Cuidados:

- Nao usar RAG para diagnostico.
- Separar conteudo educativo de decisao medica.
- Sempre citar que a Luma nao substitui profissional de saude.
- Manter uma lista de respostas bloqueadas independente do modelo.

### 4.3 MCP

MCP pode ser util para expor ferramentas controladas para a IA, por exemplo:

```txt
buscar_historico_do_ciclo
calcular_proxima_menstruacao
calcular_atraso
registrar_evento
buscar_politica_privacidade
```

Mas a IA nao deve escrever diretamente no banco sem validacao do backend. O fluxo recomendado e:

```txt
IA sugere intencao
Backend valida
Backend executa
Backend gera resposta base
IA humaniza se for seguro
```

### 4.4 Modelo local ou API externa

Para desenvolvimento local:

```txt
Ollama via Docker
```

Para producao, avaliar:

```txt
Ollama com modelo mais forte em servidor proprio
Gemini API
OpenAI API
outro provedor externo
```

Decisao deve considerar:

- latencia no WhatsApp;
- custo por mensagem;
- qualidade do parsing em portugues;
- facilidade de observabilidade;
- privacidade;
- escalabilidade;
- necessidade de GPU.

Criterios de pronto da Etapa 4:

- Pipeline de IA separado por responsabilidades.
- Prompts versionados.
- Testes para saidas estruturadas.
- Guardrails funcionando mesmo se a IA errar.
- Base RAG inicial revisada.
- Decisao documentada entre modelo local, API externa ou arquitetura hibrida.

---

# V1 - Definicao de pronto

A V1 estara pronta quando:

- Cadastro inicial estiver funcionando pelo WhatsApp.
- Metodos contraceptivos opcionais estiverem implementados.
- Fluxo de menstruacao estiver completo.
- Fluxo de gravidez estiver completo.
- Guardrails medicos estiverem implementados e testados.
- Fallbacks para mensagens nao compreendidas estiverem funcionando.
- Banco estiver persistindo ciclos, eventos, preferencias, consentimentos e gravidez.
- Testes automatizados cobrirem os fluxos principais.
- Docker Compose subir API, PostgreSQL e IA local.
- Twilio Sandbox ou provedor equivalente estiver validado.
- Logs nao expuserem conteudo sensivel por padrao.
- README de desenvolvimento estiver atualizado.

---

# V2 - SaaS e lancamento oficial

Depois da V1 validada com testes reais, o foco passa a ser transformar a Luma em SaaS.

## Etapa 5 - Website, cadastro e assinaturas

Objetivo:

Criar a camada comercial da Luma.

Escopo:

- Landing page em Next.js.
- Pagina de cadastro.
- Login da usuaria.
- Politica de privacidade.
- Termos de uso.
- Checkout de assinatura.
- Integracao com provedor de pagamento.
- Webhook de pagamento no backend.
- Tabela de assinaturas.
- Estados de assinatura:
  - trial;
  - active;
  - past_due;
  - canceled;
  - expired.

Provedores possiveis:

```txt
Asaas
Mercado Pago
Stripe Billing
Pagar.me
```

Criterios de pronto:

- Usuaria consegue criar cadastro pelo site.
- Usuaria consegue assinar.
- Backend recebe webhook de pagamento.
- Assinatura fica vinculada ao numero de WhatsApp.
- Usuaria consegue cancelar ou gerenciar assinatura.

---

## Etapa 5.1 - Validacao de assinatura no bot

Objetivo:

Fazer a Luma responder apenas usuarias autorizadas, conforme regra comercial.

Comportamento esperado:

- Ao receber mensagem, identificar numero de WhatsApp.
- Verificar se existe usuaria cadastrada.
- Verificar se ha assinatura ativa ou trial valido.
- Se nao houver cadastro, orientar a acessar o site.
- Se assinatura estiver vencida, enviar mensagem de regularizacao.
- Nao processar eventos de ciclo para usuarias sem permissao.

Mensagem exemplo:

```txt
Ainda nao encontrei uma assinatura ativa para este numero. Para usar a Luma, acesse o cadastro pelo site e vincule seu WhatsApp.
```

Criterios de pronto:

- Testes de assinatura ativa, trial, vencida e inexistente.
- Webhook do WhatsApp bloqueia uso sem assinatura.
- Logs registram decisao sem expor conteudo sensivel.

---

## Etapa 6 - Anti-spam, grupos e hardening de producao

Objetivo:

Preparar a Luma para uso publico com mais seguranca operacional.

Escopo:

- Rate limit por numero.
- Timeout temporario por excesso de mensagens.
- Bloqueio de spam.
- Deteccao de mensagens vindas de grupos.
- Resposta segura quando adicionarem a Luma em grupo.
- Validacao de assinatura antes de chamar IA.
- Evitar chamada de IA para mensagens simples.
- Observabilidade com logs estruturados.
- Monitoramento de erros.
- Auditoria administrativa.
- Rotina de exclusao/exportacao de dados.

Stack sugerida:

```txt
Redis
Hangfire
Serilog
Sentry ou Application Insights
OpenTelemetry
```

Criterios de pronto:

- Rate limit testado.
- Grupos bloqueados ou tratados com resposta especifica.
- Jobs em background funcionando.
- Alertas de erro configurados.
- Fluxos LGPD de exportacao e exclusao implementados.

---

# V2 - Definicao de pronto

A V2 estara pronta quando:

- Site permitir cadastro e assinatura.
- WhatsApp estiver vinculado a uma conta/assinatura.
- Bot bloquear usuarias sem cadastro ou sem assinatura ativa.
- Rate limit e anti-spam estiverem ativos.
- Mensagens de grupo forem tratadas com seguranca.
- Observabilidade estiver configurada.
- Fluxos LGPD principais estiverem disponiveis.
- A Luma estiver pronta para lancamento oficial controlado.

---

# Itens transversais obrigatorios

Esses itens atravessam todas as etapas.

## LGPD e privacidade

- Consentimento explicito.
- Registro de consentimento com versao.
- Revogacao de consentimento.
- Exportacao de dados.
- Exclusao de conta e dados.
- Logs sem conteudo sensivel.
- Minimizacao de dados.
- Controle de acesso administrativo.
- Politica de privacidade clara.
- Termos de uso claros.

## Qualidade e testes

- TDD para novas regras.
- xUnit para testes unitarios.
- Testcontainers para testes com PostgreSQL real.
- Testes de webhook.
- Testes de calculo de ciclo.
- Testes de guardrails.
- Testes de fallback.
- Testes de assinatura e permissoes na V2.

## Arquitetura

- Manter monolito modular no inicio.
- Separar dominio, aplicacao, infraestrutura e API conforme o projeto crescer.
- Evitar microservicos antes de necessidade real.
- Usar interfaces para WhatsApp, IA e pagamentos.
- Manter regras sensiveis no backend.

## Producao

- Usar banco gerenciado quando possivel.
- Evitar expor Ollama publicamente.
- Usar HTTPS.
- Configurar secrets fora do repositorio.
- Monitorar latencia do bot.
- Monitorar falhas de webhook.
- Ter rotina de backup do PostgreSQL.

---

# Ordem recomendada

Ordem sugerida para chegar a V1:

```txt
1.1 Metodos contraceptivos no onboarding
2. Ciclo menstrual completo
3. Gravidez completa
4. RAG/MCP/IA humanizada
```

Ordem sugerida para chegar a V2:

```txt
5. Website, cadastro e assinaturas
5.1 Validacao de assinatura no bot
6. Anti-spam, grupos e hardening de producao
```

A Etapa 4 pode comecar em paralelo de forma limitada, principalmente para melhorar parsing e humanizacao. Mesmo assim, a recomendacao e so usar RAG/MCP de forma mais ampla depois que menstruacao e gravidez estiverem bem modeladas, porque a IA precisa operar sobre ferramentas e regras estaveis.

