# Especificação inicial — Bot de Ciclo Menstrual pelo WhatsApp

## Visão geral

A ideia é criar um SaaS simples e acessível, baseado em WhatsApp, para ajudar mulheres a registrarem informações do ciclo menstrual sem precisar abrir um aplicativo específico todos os dias.

O diferencial central do produto é:

> **Um app de ciclo que você não precisa lembrar de abrir.**

A usuária interage naturalmente pelo WhatsApp, enviando mensagens como:

```txt
menstruei hoje
acabou ontem
tô com cólica forte
tive relação dia 20
quando é minha próxima menstruação?
```

O sistema interpreta essas mensagens, registra os eventos, calcula previsões e responde de forma humanizada.

A IA, como Gemini, não deve ser o “cérebro médico” do produto. Ela deve atuar principalmente como camada de interpretação e humanização.

Regra de ouro:

> **O sistema decide. A IA escreve.**

---

## Objetivo do produto

Permitir que a usuária registre e consulte dados relacionados ao ciclo menstrual por meio de uma conversa no WhatsApp.

O sistema deve ajudar com:

- Registro de início e fim da menstruação.
- Registro de intensidade do fluxo.
- Registro de sintomas.
- Registro opcional de humor e bem-estar.
- Cálculo aproximado da próxima menstruação.
- Cálculo de atraso menstrual.
- Consulta ao histórico.
- Lembretes opcionais.
- Registro opcional de relação sexual.
- Futuramente, modo gravidez.

O sistema **não deve**:

- Fazer diagnóstico médico.
- Confirmar ou descartar gravidez.
- Dizer que um sangramento é normal ou seguro.
- Substituir orientação médica.
- Incentivar uso de janela fértil como método contraceptivo.

---

## Atenção legal e LGPD

Dados de menstruação, sintomas, gravidez, relação sexual, anticoncepcional e saúde reprodutiva são dados pessoais sensíveis.

Desde o MVP, o produto deve considerar:

- Consentimento explícito e destacado.
- Política de privacidade simples e clara.
- Termo informando que o sistema não substitui profissional de saúde.
- Opção de apagar conta e dados.
- Opção de exportar dados.
- Criptografia de dados sensíveis no banco.
- Controle rigoroso de acesso.
- Logs sem conteúdo sensível sempre que possível.
- Cuidado especial com menores de idade.
- Restrição de respostas médicas/diagnósticas.

Mensagem inicial recomendada:

```txt
Oi! Eu sou sua assistente de ciclo pelo WhatsApp 🌙

Antes de começar: eu posso te ajudar a registrar menstruação, sintomas, lembretes e histórico. Não substituo orientação médica e não faço diagnósticos.

Para continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saúde menstrual.

Você aceita?
1. Aceito
2. Não aceito
```

---

# 1. Onboarding da usuária

Na primeira interação, o sistema não conhece nada sobre a usuária. O ideal é pedir apenas os dados mínimos necessários para começar.

## Dados obrigatórios iniciais

### Nome de exibição

```txt
Como devo te chamar?
```

Exemplo salvo:

```json
{
  "display_name": "Nay"
}
```

---

### Confirmação de idade

Por se tratar de saúde e vida sexual, o ideal é confirmar se a usuária tem 18 anos ou mais.

```txt
Você tem 18 anos ou mais?
1. Sim
2. Não
```

Isso pode ser importante para reduzir riscos legais e de responsabilidade.

---

### Última menstruação

Este é um dos dados mais importantes para iniciar os cálculos.

```txt
Qual foi o primeiro dia da sua última menstruação?
Pode responder tipo: "começou dia 10/04" ou "não lembro".
```

Exemplo salvo:

```json
{
  "last_period_start_date": "2026-04-10"
}
```

---

### Duração média do ciclo

```txt
Seu ciclo costuma ter quantos dias?
Se não souber, posso começar usando 28 dias e ir ajustando com o tempo.
```

O sistema não deve limitar apenas a 27, 28, 29, 30 ou 31 dias. O ideal é aceitar uma faixa razoável, por exemplo, 21 a 45 dias, e tratar valores fora disso com cuidado.

Exemplo salvo:

```json
{
  "average_cycle_length": 28
}
```

---

### Duração média da menstruação

```txt
Sua menstruação costuma durar quantos dias?
```

Exemplo salvo:

```json
{
  "average_period_length": 5
}
```

---

## Dados opcionais do onboarding

### Uso de anticoncepcional

```txt
Você usa anticoncepcional hormonal?

1. Não
2. Pílula
3. Injeção
4. DIU hormonal
5. Implante
6. Outro
7. Prefiro não informar
```

Exemplo salvo:

```json
{
  "uses_hormonal_contraceptive": true,
  "contraceptive_type": "pill"
}
```

---

### Lembretes

```txt
Você quer receber lembretes?

1. Sim, sobre próxima menstruação
2. Sim, para registrar sintomas
3. Sim, para anticoncepcional
4. Não quero lembretes
```

Exemplo salvo:

```json
{
  "reminders_enabled": true,
  "reminder_types": ["next_period", "symptoms"]
}
```

---

## Fluxo completo sugerido de primeira conversa

```txt
Oi! Eu sou sua assistente de ciclo pelo WhatsApp 🌙

Antes de começar: eu posso te ajudar a registrar menstruação, sintomas, lembretes e histórico. Não substituo orientação médica e não faço diagnósticos.

Para continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saúde menstrual.

Você aceita?
1. Aceito
2. Não aceito
```

```txt
Como devo te chamar?
```

```txt
Você tem 18 anos ou mais?
1. Sim
2. Não
```

```txt
Qual foi o primeiro dia da sua última menstruação?
Pode responder tipo: "começou dia 10/04" ou "não lembro".
```

```txt
Seu ciclo costuma ter quantos dias?
Se não souber, posso começar usando 28 dias e ir ajustando com o tempo.
```

```txt
Sua menstruação costuma durar quantos dias?
```

```txt
Pronto ✅

Agora você pode me mandar coisas como:

"menstruei hoje"
"acabou ontem"
"tô com cólica forte"
"tive relação dia 20"
"quando é minha próxima menstruação?"
```

---

# 2. Modelo principal: ciclo menstrual

O ciclo menstrual deve ser a entidade central do sistema.

Um ciclo começa quando a usuária registra o início da menstruação.

Exemplo de ciclo:

```json
{
  "id": "cycle_123",
  "user_id": "user_123",
  "start_date": "2026-04-24",
  "end_date": null,
  "status": "ongoing",
  "predicted_next_period_date": "2026-05-22"
}
```

## Status possíveis do ciclo

```txt
ongoing
finished
unknown
```

- `ongoing`: ciclo atual em andamento.
- `finished`: ciclo encerrado.
- `unknown`: dados insuficientes ou inconsistentes.

---

# 3. Event log

Em vez de salvar tudo diretamente em campos fixos no ciclo, é melhor criar um registro de eventos.

Isso torna o sistema mais flexível e auditável.

Exemplo de evento:

```json
{
  "id": "event_123",
  "user_id": "user_123",
  "cycle_id": "cycle_123",
  "type": "period_start",
  "date": "2026-04-24",
  "metadata": {
    "flow_intensity": "medium",
    "notes": null
  }
}
```

## Tipos iniciais de eventos

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

# 4. Registro de menstruação

## Início da menstruação

Usuária envia:

```txt
menstruei hoje
```

O sistema interpreta:

```json
{
  "intent": "period_start",
  "date": "2026-04-24"
}
```

Resposta sugerida:

```txt
Registrei o início da sua menstruação hoje ✅

Como está o fluxo?
1. Leve
2. Médio
3. Intenso
4. Prefiro não informar
```

Usuária responde:

```txt
2
```

Evento salvo:

```json
{
  "type": "flow_update",
  "date": "2026-04-24",
  "metadata": {
    "flow_intensity": "medium"
  }
}
```

---

## Intensidade do fluxo

Valores possíveis:

```txt
light
medium
intense
unknown
```

Representação amigável:

```txt
leve
médio
intenso
prefiro não informar
```

A usuária pode atualizar o fluxo durante a menstruação:

```txt
hoje tá bem forte
```

O sistema registra um novo `flow_update`.

Para o MVP, a regra mais simples é:

> **1 intensidade por dia. Se a usuária atualizar no mesmo dia, sobrescreve a intensidade daquele dia.**

Exemplo de resposta:

```txt
Você já registrou fluxo médio para hoje.
Quer alterar para intenso?
```

Se confirmar:

```txt
Atualizado ✅
Hoje ficou registrado como fluxo intenso.
```

---

## Fim da menstruação

Usuária envia:

```txt
parou hoje
```

ou:

```txt
minha menstruação acabou ontem
```

O sistema interpreta:

```json
{
  "intent": "period_end",
  "date": "2026-04-23"
}
```

Atualização do ciclo:

```json
{
  "end_date": "2026-04-23",
  "status": "finished"
}
```

Resposta sugerida:

```txt
Registrei que sua menstruação terminou em 23/04 ✅

Ela durou 5 dias neste ciclo.
Com base nisso, sua próxima menstruação está prevista para perto de 22/05.
```

Sempre usar linguagem de estimativa:

```txt
está prevista para perto de...
pode acontecer por volta de...
pela sua média atual...
```

Evitar:

```txt
vai descer no dia...
com certeza será em...
```

---

# 5. Atraso menstrual

O atraso não precisa ser um evento manual salvo como fonte de verdade.

Ele pode ser calculado automaticamente:

```txt
expected_period_date = last_period_start + average_cycle_length
delay_days = today - expected_period_date
```

Exemplo:

```json
{
  "expected_period_date": "2026-04-22",
  "today": "2026-04-24",
  "delay_days": 2
}
```

Usuária pergunta:

```txt
minha menstruação está atrasada?
```

Resposta sugerida:

```txt
Pela sua previsão atual, sua menstruação está cerca de 2 dias atrasada.

Isso pode acontecer por vários motivos, como variação natural do ciclo, estresse, alterações de rotina ou outros fatores. Se houver chance de gravidez ou sintomas preocupantes, o ideal é fazer um teste ou procurar orientação médica.
```

Se a usuária mandar:

```txt
tô atrasada e com cólica
```

Isso pode virar um evento do tipo `symptom` ou `note`, mas o atraso em si deve continuar sendo calculado pelo sistema.

---

# 6. Relação sexual

Esse dado é bastante sensível. Deve ser opcional e tratado com cautela.

Uso principal:

- Histórico pessoal da usuária.
- Responder perguntas como “quando foi minha última relação?”.
- Exibir eventos no calendário pessoal.

Não usar para diagnóstico automático.

Exemplo de evento:

```json
{
  "type": "sexual_activity",
  "date": "2026-04-20",
  "metadata": {
    "protected": "unknown",
    "notes": null
  }
}
```

Campos possíveis:

```txt
date
protected: yes/no/unknown/prefer_not_say
contraceptive_method: condom/pill/other/unknown
notes
```

Para MVP, simplificar:

```json
{
  "date": "2026-04-20",
  "type": "sexual_activity"
}
```

Resposta segura:

```txt
Registrei a relação em 20/04 ✅

Esse dado fica salvo apenas para seu histórico. Eu não uso isso para afirmar gravidez ou diagnóstico.
```

---

# 7. Sintomas

Sintomas são uma parte importante do valor percebido do produto.

Usuária envia:

```txt
tive cólica forte hoje
```

Evento salvo:

```json
{
  "type": "symptom",
  "date": "2026-04-24",
  "metadata": {
    "symptom": "cramp",
    "intensity": "strong"
  }
}
```

## Sintomas úteis para começar

```txt
cólica
dor de cabeça
náusea
sensibilidade nos seios
inchaço
acne
dor lombar
sangramento fora do período
corrimento
alteração de humor
cansaço
insônia
desejo alimentar
```

## Intensidade

```txt
leve
moderado
forte
```

Exemplo de interpretação por IA:

Usuária:

```txt
hoje tô com uma cólica absurda
```

JSON interpretado:

```json
{
  "symptom": "cramp",
  "intensity": "strong"
}
```

---

# 8. Humor e bem-estar

Também pode gerar valor, sem necessariamente entrar em diagnóstico.

Usuária envia:

```txt
hoje tô irritada
```

Evento salvo:

```json
{
  "type": "mood",
  "date": "2026-04-24",
  "metadata": {
    "mood": "irritable"
  }
}
```

Opções iniciais:

```txt
irritada
triste
ansiosa
bem
sensível
cansada
com energia
```

Resposta futura possível:

```txt
Nos últimos 3 ciclos, você registrou irritação perto do início da menstruação.
```

Melhor ainda:

```txt
Parece haver um padrão nos seus registros de humor perto do início da menstruação.
```

Evitar linguagem diagnóstica:

```txt
Você tem TPM forte.
```

---

# 9. Gravidez

Recomendação: deixar para uma versão 2.

O modo gravidez é útil, mas aumenta a responsabilidade do produto.

Se a usuária disser:

```txt
descobri que estou grávida
```

Resposta sugerida:

```txt
Parabéns pela notícia 💛

Para eu organizar seu acompanhamento, você sabe alguma dessas informações?

1. Data da última menstruação
2. Quantas semanas de gravidez
3. Data provável do parto
4. Ainda não sei
```

Exemplo de estrutura:

```json
{
  "pregnancy_status": "pregnant",
  "pregnancy_start_reference": "last_period",
  "last_period_date": "2026-03-01",
  "estimated_due_date": "2026-12-06"
}
```

## Eventos de gravidez

```txt
pregnancy_positive
pregnancy_bleeding
pregnancy_symptom
prenatal_appointment
ultrasound
pregnancy_note
```

Exemplo de sangramento na gravidez:

Usuária:

```txt
tive sangramento hoje
```

Resposta segura:

```txt
Registrei o sangramento de hoje.

Sangramentos na gravidez podem ter várias causas, algumas simples e outras que precisam de avaliação. Como você está grávida, é mais seguro entrar em contato com seu médico/obstetra, especialmente se o sangramento for intenso, vier com dor forte, tontura, febre ou mal-estar.
```

Esse tipo de resposta deve ser guardrail fixo, não apenas IA solta.

---

# 10. Cálculos do sistema

Com os dados registrados, o sistema pode calcular:

```txt
próxima menstruação prevista
dias de atraso
duração média do ciclo
duração média da menstruação
variação dos ciclos
última menstruação
última relação registrada
sintomas mais frequentes
intensidade média do fluxo
eventos por mês
possível janela fértil estimada
data provável do parto, se grávida
idade gestacional estimada, se grávida
```

## Cuidados com janela fértil

Se exibir janela fértil, usar sempre como estimativa.

Mensagem recomendada:

```txt
Essa é apenas uma estimativa baseada nos seus registros. Ela não deve ser usada como método contraceptivo.
```

---

# 11. Respostas que o bot não deve dar

O sistema deve bloquear ou redirecionar respostas como:

```txt
Você está grávida.
Você não está grávida.
Você está com infecção.
Você tem endometriose.
Pode ter relação sem proteção hoje.
Você está no período seguro.
Não precisa procurar médico.
Esse sangramento é normal.
```

Resposta segura padrão:

```txt
Não consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnóstico ou decisão médica o ideal é procurar um profissional de saúde.
```

---

# 12. Papel do Gemini / IA

A IA deve ser usada em pontos específicos.

## 1. Interpretação da mensagem

Usuária:

```txt
desceu ontem e hoje tá vindo muito
```

Gemini retorna:

```json
{
  "intent": "period_start",
  "date": "2026-04-23",
  "entities": {
    "flow_intensity": "intense"
  },
  "confidence": 0.92
}
```

---

## 2. Humanização da resposta

Sistema determinístico decide:

```json
{
  "action": "period_start_created",
  "date": "2026-04-23",
  "flow": "intense"
}
```

Gemini transforma em:

```txt
Registrei que sua menstruação começou ontem, 23/04, com fluxo intenso ✅
Vou considerar esse como o início do seu ciclo atual.
```

---

## 3. Perguntas gerais seguras

Usuária:

```txt
é normal atrasar 2 dias?
```

Resposta possível:

```txt
Um atraso pequeno pode acontecer por variações naturais do ciclo, estresse, sono, rotina, uso de medicamentos e outros fatores. Se houver chance de gravidez, vale considerar um teste. Se o atraso persistir, vier com dor forte, sangramento intenso ou outros sintomas preocupantes, procure orientação médica.
```

---

# 13. Modelo de dados inicial

## users

```json
{
  "id": "user_123",
  "phone": "+5516999999999",
  "display_name": "Nay",
  "birth_year": 1998,
  "is_adult_confirmed": true,
  "created_at": "2026-04-24T20:00:00Z"
}
```

---

## user_preferences

```json
{
  "user_id": "user_123",
  "average_cycle_length": 28,
  "average_period_length": 5,
  "uses_hormonal_contraceptive": false,
  "reminders_enabled": true,
  "language": "pt-BR"
}
```

---

## cycles

```json
{
  "id": "cycle_123",
  "user_id": "user_123",
  "start_date": "2026-04-24",
  "end_date": null,
  "status": "ongoing",
  "cycle_number": 4
}
```

---

## events

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

## pregnancies

```json
{
  "id": "pregnancy_123",
  "user_id": "user_123",
  "status": "active",
  "last_period_date": "2026-03-01",
  "estimated_due_date": "2026-12-06",
  "created_at": "2026-04-24T20:00:00Z"
}
```

---

# 14. Arquitetura lógica

Fluxo geral:

```txt
WhatsApp
  ↓
Parser com IA ou regras
  ↓
Validador de segurança
  ↓
Motor de eventos
  ↓
Banco de dados
  ↓
Motor de cálculo
  ↓
Resposta determinística
  ↓
Gemini humaniza
  ↓
WhatsApp
```

## Componentes

### WhatsApp API

Responsável por receber e enviar mensagens.

Pode ser implementado com:

- WhatsApp Cloud API oficial.
- Z-API.
- Outro provedor compatível.

---

### Parser

Interpreta mensagens naturais.

Exemplos:

```txt
menstruei hoje
```

```json
{
  "intent": "period_start",
  "date": "today"
}
```

```txt
acabou ontem
```

```json
{
  "intent": "period_end",
  "date": "yesterday"
}
```

---

### Validador de segurança

Antes de salvar ou responder, verifica:

- A usuária existe?
- Está com assinatura ativa?
- Deu consentimento?
- A mensagem envolve risco médico?
- Deve bloquear diagnóstico?
- Deve orientar procurar médico?
- Está em rate limit?

---

### Motor de eventos

Transforma intenções em eventos persistidos.

Exemplo:

```json
{
  "type": "period_start",
  "date": "2026-04-24"
}
```

---

### Motor de cálculo

Calcula previsões e métricas:

- Próxima menstruação.
- Atraso.
- Duração média.
- Padrões de sintomas.
- Histórico.

---

### Camada de resposta

Gera uma resposta base determinística e, se necessário, manda para IA humanizar.

---

# 15. Rate limit e anti-spam

Para evitar abuso e custo excessivo:

- Limitar mensagens por minuto.
- Limitar mensagens por dia.
- Detectar spam.
- Timeout temporário.
- Não chamar IA para toda mensagem simples.

Exemplo de regra:

```txt
5 mensagens em menos de 10 segundos = timeout temporário
```

Resposta:

```txt
Recebi muitas mensagens em sequência. Vou pausar por alguns minutos para evitar erros no registro. Daqui a pouco você pode continuar normalmente.
```

Para casos mais agressivos:

```txt
Detectei muitas mensagens em pouco tempo. Por segurança, sua conversa ficará pausada até amanhã.
```

---

# 16. MVP recomendado

## Essencial para o MVP

```txt
cadastro por WhatsApp
consentimento
início da menstruação
fim da menstruação
intensidade do fluxo
sintomas
previsão da próxima menstruação
atraso calculado
perguntas simples sobre histórico
```

## Opcional no MVP

```txt
relação sexual
lembretes
anticoncepcional
humor
```

## Deixar para versão 2

```txt
modo gravidez
relatórios avançados
janela fértil
parceiro/médico
exportação PDF
integração com calendário
painel web avançado
```

---

# 17. Landing page — posicionamento sugerido

## Proposta de valor

```txt
Controle seu ciclo pelo WhatsApp, sem precisar abrir mais um aplicativo.
```

## Headline alternativa

```txt
Seu ciclo, registrado em uma conversa simples.
```

## Subheadline

```txt
Anote menstruação, sintomas, fluxo e lembretes direto pelo WhatsApp. Simples, discreto e fácil de manter no dia a dia.
```

## Benefícios principais

```txt
Não precisa lembrar de abrir app
Registre tudo por mensagem
Receba previsões e lembretes
Consulte seu histórico quando quiser
Sem diagnósticos, sem complicação
Privacidade e controle dos seus dados
```

## Exemplos para mostrar na LP

```txt
Você: menstruei hoje
Bot: Registrei o início da sua menstruação hoje ✅ Como está o fluxo?
```

```txt
Você: tô com cólica forte
Bot: Registrei cólica forte para hoje. Espero que você fique melhor 💛
```

```txt
Você: quando é minha próxima menstruação?
Bot: Pela sua média atual, ela está prevista para perto de 22/05.
```

## Aviso de segurança na LP

```txt
Este sistema ajuda você a organizar seus registros pessoais de ciclo menstrual. Ele não substitui orientação médica e não realiza diagnósticos.
```

---

# 18. Possíveis nomes de produto

Ideias iniciais:

```txt
Cicla
LunaBot
Meu Ciclo Zap
CicloZap
Lua
Lunari
Ciclo Fácil
Ciclo no Zap
MinaCiclo
Clara Ciclo
```

---

# 19. Resumo final

A ideia tem potencial porque resolve um problema simples e real:

> Aplicativos de ciclo dependem da usuária lembrar de abrir o app. O WhatsApp já faz parte da rotina.

A estratégia ideal é começar pequeno:

1. Registrar menstruação.
2. Registrar fim da menstruação.
3. Registrar fluxo.
4. Registrar sintomas.
5. Calcular próxima menstruação.
6. Responder histórico básico.
7. Usar IA apenas para entender mensagens naturais e humanizar respostas.

Depois, evoluir para:

- Lembretes.
- Anticoncepcional.
- Humor.
- Relação sexual.
- Modo gravidez.
- Relatórios.
- Painel web.

O principal cuidado é não transformar o bot em médico. O produto deve ser um **organizador pessoal de ciclo menstrual por WhatsApp**, não uma ferramenta de diagnóstico.
