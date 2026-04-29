# EspecificaÃ§Ã£o inicial â€” Bot de Ciclo Menstrual pelo WhatsApp

> Atualização de escopo para V1.0.0 - 2026-04-28
>
> O nucleo transacional da Luma já contempla cadastro, ciclo menstrual, relação sexual, gravidez, histórico básico e guardrails principais. Para a primeira versão de producao, a Luma não deve ser apenas um fluxo fixo de perguntas e respostas. Ela deve evoluir para uma assistente conversacional com orquestracao de IA, RAG e tools/MCP controladas pelo backend.
>
> Regra central atualizada:
>
> **O backend decide. A IA entende, conversa e escreve.**
>
> A IA pode interpretar mensagens fora de ordem, consultar conhecimento seguro, sugerir chamadas de ferramentas e humanizar respostas. O backend continua sendo a fonte de verdade para consentimento, LGPD, regras médicas, calculos, persistência e validação de segurança.

---

## Atualização V1.0.0 - Luma como assistente inteligente

Para fechar a V1.0.0, a Luma deve lidar com situações em que a usuaria não segue o fluxo esperado.

Exemplo:

```txt
Estado atual: aguardando nome da usuaria
Usuaria: menstruei hoje
```

Comportamento esperado:

```txt
Entendi. Já vi que você quer registrar que sua menstruação comecou hoje.
Antes disso, preciso terminar seu cadastro rapidinho para salvar tudo certinho e com segurança.
Como devo te chamar?
```

Depois do cadastro:

```txt
Você tinha me contado que menstruou hoje. Quer que eu registre isso agora?
```

Se a usuaria confirmar, o backend registra o evento.

Isso exige:

- memoria de intenção pendente;
- interpretacao contextual;
- confirmação antes de gravar dados sensiveis fora do fluxo esperado;
- RAG para respostas educativas e institucionais;
- tools/MCP para leitura e escrita controladas;
- backend autoritativo;
- guardrails fixos para LGPD e saúde.

Fluxo alvo:

```txt
Mensagem da usuaria
  ->
IA interpreta intenção, contexto e estado atual
  ->
Backend valida segurança, consentimento e regras de negocio
  ->
Backend executa tools autorizadas
  ->
RAG fornece conteúdo seguro quando necessário
  ->
IA escreve resposta acolhedora
  ->
WhatsApp
```

Essa camada inteligente e considerada parte obrigatoria da V1.0.0.

---

## VisÃ£o geral

A ideia Ã© criar um SaaS simples e acessÃ­vel, baseado em WhatsApp, para ajudar mulheres a registrarem informaÃ§Ãµes do ciclo menstrual sem precisar abrir um aplicativo especÃ­fico todos os dias.

O diferencial central do produto Ã©:

> **Um app de ciclo que vocÃª nÃ£o precisa lembrar de abrir.**

A usuÃ¡ria interage naturalmente pelo WhatsApp, enviando mensagens como:

```txt
menstruei hoje
acabou ontem
tÃ´ com cÃ³lica forte
tive relaÃ§Ã£o dia 20
quando Ã© minha prÃ³xima menstruaÃ§Ã£o?
```

O sistema interpreta essas mensagens, registra os eventos, calcula previsÃµes e responde de forma humanizada.

A IA, como Gemini, nÃ£o deve ser o â€œcÃ©rebro mÃ©dicoâ€ do produto. Ela deve atuar principalmente como camada de interpretaÃ§Ã£o e humanizaÃ§Ã£o.

Regra de ouro:

> **O sistema decide. A IA escreve.**

---

## Objetivo do produto

Permitir que a usuÃ¡ria registre e consulte dados relacionados ao ciclo menstrual por meio de uma conversa no WhatsApp.

O sistema deve ajudar com:

- Registro de inÃ­cio e fim da menstruaÃ§Ã£o.
- Registro de intensidade do fluxo.
- Registro de sintomas.
- Registro opcional de humor e bem-estar.
- CÃ¡lculo aproximado da prÃ³xima menstruaÃ§Ã£o.
- CÃ¡lculo de atraso menstrual.
- Consulta ao histÃ³rico.
- Lembretes opcionais.
- Registro opcional de relaÃ§Ã£o sexual.
- Futuramente, modo gravidez.

O sistema **nÃ£o deve**:

- Fazer diagnÃ³stico mÃ©dico.
- Confirmar ou descartar gravidez.
- Dizer que um sangramento Ã© normal ou seguro.
- Substituir orientaÃ§Ã£o mÃ©dica.
- Incentivar uso de janela fÃ©rtil como mÃ©todo contraceptivo.

---

## AtenÃ§Ã£o legal e LGPD

Dados de menstruaÃ§Ã£o, sintomas, gravidez, relaÃ§Ã£o sexual, anticoncepcional e saÃºde reprodutiva sÃ£o dados pessoais sensÃ­veis.

Desde o MVP, o produto deve considerar:

- Consentimento explÃ­cito e destacado.
- PolÃ­tica de privacidade simples e clara.
- Termo informando que o sistema nÃ£o substitui profissional de saÃºde.
- OpÃ§Ã£o de apagar conta e dados.
- OpÃ§Ã£o de exportar dados.
- Criptografia de dados sensÃ­veis no banco.
- Controle rigoroso de acesso.
- Logs sem conteÃºdo sensÃ­vel sempre que possÃ­vel.
- Cuidado especial com menores de idade.
- RestriÃ§Ã£o de respostas mÃ©dicas/diagnÃ³sticas.

Mensagem inicial recomendada:

```txt
Oi! Eu sou sua assistente de ciclo pelo WhatsApp ðŸŒ™

Antes de comeÃ§ar: eu posso te ajudar a registrar menstruaÃ§Ã£o, sintomas, lembretes e histÃ³rico. NÃ£o substituo orientaÃ§Ã£o mÃ©dica e nÃ£o faÃ§o diagnÃ³sticos.

Para continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saÃºde menstrual.

VocÃª aceita?
1. Aceito
2. NÃ£o aceito
```

---

# 1. Onboarding da usuÃ¡ria

Na primeira interaÃ§Ã£o, o sistema nÃ£o conhece nada sobre a usuÃ¡ria. O ideal Ã© pedir apenas os dados mÃ­nimos necessÃ¡rios para comeÃ§ar.

## Dados obrigatÃ³rios iniciais

### Nome de exibiÃ§Ã£o

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

### ConfirmaÃ§Ã£o de idade

Por se tratar de saÃºde e vida sexual, o ideal Ã© confirmar se a usuÃ¡ria tem 18 anos ou mais.

```txt
VocÃª tem 18 anos ou mais?
1. Sim
2. NÃ£o
```

Isso pode ser importante para reduzir riscos legais e de responsabilidade.

---

### Ãšltima menstruaÃ§Ã£o

Este Ã© um dos dados mais importantes para iniciar os cÃ¡lculos.

```txt
Qual foi o primeiro dia da sua Ãºltima menstruaÃ§Ã£o?
Pode responder tipo: "comeÃ§ou dia 10/04" ou "nÃ£o lembro".
```

Exemplo salvo:

```json
{
  "last_period_start_date": "2026-04-10"
}
```

---

### DuraÃ§Ã£o mÃ©dia do ciclo

```txt
Seu ciclo costuma ter quantos dias?
Se nÃ£o souber, posso comeÃ§ar usando 28 dias e ir ajustando com o tempo.
```

O sistema nÃ£o deve limitar apenas a 27, 28, 29, 30 ou 31 dias. O ideal Ã© aceitar uma faixa razoÃ¡vel, por exemplo, 21 a 45 dias, e tratar valores fora disso com cuidado.

Exemplo salvo:

```json
{
  "average_cycle_length": 28
}
```

---

### DuraÃ§Ã£o mÃ©dia da menstruaÃ§Ã£o

```txt
Sua menstruaÃ§Ã£o costuma durar quantos dias?
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
VocÃª usa anticoncepcional hormonal?

1. NÃ£o
2. PÃ­lula
3. InjeÃ§Ã£o
4. DIU hormonal
5. Implante
6. Outro
7. Prefiro nÃ£o informar
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
VocÃª quer receber lembretes?

1. Sim, sobre prÃ³xima menstruaÃ§Ã£o
2. Sim, para registrar sintomas
3. Sim, para anticoncepcional
4. NÃ£o quero lembretes
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
Oi! Eu sou sua assistente de ciclo pelo WhatsApp ðŸŒ™

Antes de comeÃ§ar: eu posso te ajudar a registrar menstruaÃ§Ã£o, sintomas, lembretes e histÃ³rico. NÃ£o substituo orientaÃ§Ã£o mÃ©dica e nÃ£o faÃ§o diagnÃ³sticos.

Para continuar, preciso do seu consentimento para armazenar dados relacionados ao seu ciclo, sintomas e saÃºde menstrual.

VocÃª aceita?
1. Aceito
2. NÃ£o aceito
```

```txt
Como devo te chamar?
```

```txt
VocÃª tem 18 anos ou mais?
1. Sim
2. NÃ£o
```

```txt
Qual foi o primeiro dia da sua Ãºltima menstruaÃ§Ã£o?
Pode responder tipo: "comeÃ§ou dia 10/04" ou "nÃ£o lembro".
```

```txt
Seu ciclo costuma ter quantos dias?
Se nÃ£o souber, posso comeÃ§ar usando 28 dias e ir ajustando com o tempo.
```

```txt
Sua menstruaÃ§Ã£o costuma durar quantos dias?
```

```txt
Pronto âœ…

Agora vocÃª pode me mandar coisas como:

"menstruei hoje"
"acabou ontem"
"tÃ´ com cÃ³lica forte"
"tive relaÃ§Ã£o dia 20"
"quando Ã© minha prÃ³xima menstruaÃ§Ã£o?"
```

---

# 2. Modelo principal: ciclo menstrual

O ciclo menstrual deve ser a entidade central do sistema.

Um ciclo comeÃ§a quando a usuÃ¡ria registra o inÃ­cio da menstruaÃ§Ã£o.

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

## Status possÃ­veis do ciclo

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

Em vez de salvar tudo diretamente em campos fixos no ciclo, Ã© melhor criar um registro de eventos.

Isso torna o sistema mais flexÃ­vel e auditÃ¡vel.

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

# 4. Registro de menstruaÃ§Ã£o

## InÃ­cio da menstruaÃ§Ã£o

UsuÃ¡ria envia:

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
Registrei o inÃ­cio da sua menstruaÃ§Ã£o hoje âœ…

Como estÃ¡ o fluxo?
1. Leve
2. MÃ©dio
3. Intenso
4. Prefiro nÃ£o informar
```

UsuÃ¡ria responde:

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

Valores possÃ­veis:

```txt
light
medium
intense
unknown
```

RepresentaÃ§Ã£o amigÃ¡vel:

```txt
leve
mÃ©dio
intenso
prefiro nÃ£o informar
```

A usuÃ¡ria pode atualizar o fluxo durante a menstruaÃ§Ã£o:

```txt
hoje tÃ¡ bem forte
```

O sistema registra um novo `flow_update`.

Para o MVP, a regra mais simples Ã©:

> **1 intensidade por dia. Se a usuÃ¡ria atualizar no mesmo dia, sobrescreve a intensidade daquele dia.**

Exemplo de resposta:

```txt
VocÃª jÃ¡ registrou fluxo mÃ©dio para hoje.
Quer alterar para intenso?
```

Se confirmar:

```txt
Atualizado âœ…
Hoje ficou registrado como fluxo intenso.
```

---

## Fim da menstruaÃ§Ã£o

UsuÃ¡ria envia:

```txt
parou hoje
```

ou:

```txt
minha menstruaÃ§Ã£o acabou ontem
```

O sistema interpreta:

```json
{
  "intent": "period_end",
  "date": "2026-04-23"
}
```

AtualizaÃ§Ã£o do ciclo:

```json
{
  "end_date": "2026-04-23",
  "status": "finished"
}
```

Resposta sugerida:

```txt
Registrei que sua menstruaÃ§Ã£o terminou em 23/04 âœ…

Ela durou 5 dias neste ciclo.
Com base nisso, sua prÃ³xima menstruaÃ§Ã£o estÃ¡ prevista para perto de 22/05.
```

Sempre usar linguagem de estimativa:

```txt
estÃ¡ prevista para perto de...
pode acontecer por volta de...
pela sua mÃ©dia atual...
```

Evitar:

```txt
vai descer no dia...
com certeza serÃ¡ em...
```

---

# 5. Atraso menstrual

O atraso nÃ£o precisa ser um evento manual salvo como fonte de verdade.

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

UsuÃ¡ria pergunta:

```txt
minha menstruaÃ§Ã£o estÃ¡ atrasada?
```

Resposta sugerida:

```txt
Pela sua previsÃ£o atual, sua menstruaÃ§Ã£o estÃ¡ cerca de 2 dias atrasada.

Isso pode acontecer por vÃ¡rios motivos, como variaÃ§Ã£o natural do ciclo, estresse, alteraÃ§Ãµes de rotina ou outros fatores. Se houver chance de gravidez ou sintomas preocupantes, o ideal Ã© fazer um teste ou procurar orientaÃ§Ã£o mÃ©dica.
```

Se a usuÃ¡ria mandar:

```txt
tÃ´ atrasada e com cÃ³lica
```

Isso pode virar um evento do tipo `symptom` ou `note`, mas o atraso em si deve continuar sendo calculado pelo sistema.

---

# 6. RelaÃ§Ã£o sexual

Esse dado Ã© bastante sensÃ­vel. Deve ser opcional e tratado com cautela.

Uso principal:

- HistÃ³rico pessoal da usuÃ¡ria.
- Responder perguntas como â€œquando foi minha Ãºltima relaÃ§Ã£o?â€.
- Exibir eventos no calendÃ¡rio pessoal.

NÃ£o usar para diagnÃ³stico automÃ¡tico.

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

Campos possÃ­veis:

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
Registrei a relaÃ§Ã£o em 20/04 âœ…

Esse dado fica salvo apenas para seu histÃ³rico. Eu nÃ£o uso isso para afirmar gravidez ou diagnÃ³stico.
```

---

# 7. Sintomas

Sintomas sÃ£o uma parte importante do valor percebido do produto.

UsuÃ¡ria envia:

```txt
tive cÃ³lica forte hoje
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

## Sintomas Ãºteis para comeÃ§ar

```txt
cÃ³lica
dor de cabeÃ§a
nÃ¡usea
sensibilidade nos seios
inchaÃ§o
acne
dor lombar
sangramento fora do perÃ­odo
corrimento
alteraÃ§Ã£o de humor
cansaÃ§o
insÃ´nia
desejo alimentar
```

## Intensidade

```txt
leve
moderado
forte
```

Exemplo de interpretaÃ§Ã£o por IA:

UsuÃ¡ria:

```txt
hoje tÃ´ com uma cÃ³lica absurda
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

TambÃ©m pode gerar valor, sem necessariamente entrar em diagnÃ³stico.

UsuÃ¡ria envia:

```txt
hoje tÃ´ irritada
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

OpÃ§Ãµes iniciais:

```txt
irritada
triste
ansiosa
bem
sensÃ­vel
cansada
com energia
```

Resposta futura possÃ­vel:

```txt
Nos Ãºltimos 3 ciclos, vocÃª registrou irritaÃ§Ã£o perto do inÃ­cio da menstruaÃ§Ã£o.
```

Melhor ainda:

```txt
Parece haver um padrÃ£o nos seus registros de humor perto do inÃ­cio da menstruaÃ§Ã£o.
```

Evitar linguagem diagnÃ³stica:

```txt
VocÃª tem TPM forte.
```

---

# 9. Gravidez

RecomendaÃ§Ã£o: deixar para uma versÃ£o 2.

O modo gravidez Ã© Ãºtil, mas aumenta a responsabilidade do produto.

Se a usuÃ¡ria disser:

```txt
descobri que estou grÃ¡vida
```

Resposta sugerida:

```txt
ParabÃ©ns pela notÃ­cia ðŸ’›

Para eu organizar seu acompanhamento, vocÃª sabe alguma dessas informaÃ§Ãµes?

1. Data da Ãºltima menstruaÃ§Ã£o
2. Quantas semanas de gravidez
3. Data provÃ¡vel do parto
4. Ainda nÃ£o sei
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

UsuÃ¡ria:

```txt
tive sangramento hoje
```

Resposta segura:

```txt
Registrei o sangramento de hoje.

Sangramentos na gravidez podem ter vÃ¡rias causas, algumas simples e outras que precisam de avaliaÃ§Ã£o. Como vocÃª estÃ¡ grÃ¡vida, Ã© mais seguro entrar em contato com seu mÃ©dico/obstetra, especialmente se o sangramento for intenso, vier com dor forte, tontura, febre ou mal-estar.
```

Esse tipo de resposta deve ser guardrail fixo, nÃ£o apenas IA solta.

---

# 10. CÃ¡lculos do sistema

Com os dados registrados, o sistema pode calcular:

```txt
prÃ³xima menstruaÃ§Ã£o prevista
dias de atraso
duraÃ§Ã£o mÃ©dia do ciclo
duraÃ§Ã£o mÃ©dia da menstruaÃ§Ã£o
variaÃ§Ã£o dos ciclos
Ãºltima menstruaÃ§Ã£o
Ãºltima relaÃ§Ã£o registrada
sintomas mais frequentes
intensidade mÃ©dia do fluxo
eventos por mÃªs
possÃ­vel janela fÃ©rtil estimada
data provÃ¡vel do parto, se grÃ¡vida
idade gestacional estimada, se grÃ¡vida
```

## Cuidados com janela fÃ©rtil

Se exibir janela fÃ©rtil, usar sempre como estimativa.

Mensagem recomendada:

```txt
Essa Ã© apenas uma estimativa baseada nos seus registros. Ela nÃ£o deve ser usada como mÃ©todo contraceptivo.
```

---

# 11. Respostas que o bot nÃ£o deve dar

O sistema deve bloquear ou redirecionar respostas como:

```txt
VocÃª estÃ¡ grÃ¡vida.
VocÃª nÃ£o estÃ¡ grÃ¡vida.
VocÃª estÃ¡ com infecÃ§Ã£o.
VocÃª tem endometriose.
Pode ter relaÃ§Ã£o sem proteÃ§Ã£o hoje.
VocÃª estÃ¡ no perÃ­odo seguro.
NÃ£o precisa procurar mÃ©dico.
Esse sangramento Ã© normal.
```

Resposta segura padrÃ£o:

```txt
NÃ£o consigo confirmar isso por aqui. Posso te ajudar a organizar seus registros, mas para diagnÃ³stico ou decisÃ£o mÃ©dica o ideal Ã© procurar um profissional de saÃºde.
```

---

# 12. Papel do Gemini / IA

A IA deve ser usada em pontos especÃ­ficos.

## 1. InterpretaÃ§Ã£o da mensagem

UsuÃ¡ria:

```txt
desceu ontem e hoje tÃ¡ vindo muito
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

## 2. HumanizaÃ§Ã£o da resposta

Sistema determinÃ­stico decide:

```json
{
  "action": "period_start_created",
  "date": "2026-04-23",
  "flow": "intense"
}
```

Gemini transforma em:

```txt
Registrei que sua menstruaÃ§Ã£o comeÃ§ou ontem, 23/04, com fluxo intenso âœ…
Vou considerar esse como o inÃ­cio do seu ciclo atual.
```

---

## 3. Perguntas gerais seguras

UsuÃ¡ria:

```txt
Ã© normal atrasar 2 dias?
```

Resposta possÃ­vel:

```txt
Um atraso pequeno pode acontecer por variaÃ§Ãµes naturais do ciclo, estresse, sono, rotina, uso de medicamentos e outros fatores. Se houver chance de gravidez, vale considerar um teste. Se o atraso persistir, vier com dor forte, sangramento intenso ou outros sintomas preocupantes, procure orientaÃ§Ã£o mÃ©dica.
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

# 14. Arquitetura lÃ³gica

Fluxo geral:

```txt
WhatsApp
  â†“
Parser com IA ou regras
  â†“
Validador de seguranÃ§a
  â†“
Motor de eventos
  â†“
Banco de dados
  â†“
Motor de cÃ¡lculo
  â†“
Resposta determinÃ­stica
  â†“
Gemini humaniza
  â†“
WhatsApp
```

## Componentes

### WhatsApp API

ResponsÃ¡vel por receber e enviar mensagens.

Pode ser implementado com:

- WhatsApp Cloud API oficial.
- Z-API.
- Outro provedor compatÃ­vel.

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

### Validador de seguranÃ§a

Antes de salvar ou responder, verifica:

- A usuÃ¡ria existe?
- EstÃ¡ com assinatura ativa?
- Deu consentimento?
- A mensagem envolve risco mÃ©dico?
- Deve bloquear diagnÃ³stico?
- Deve orientar procurar mÃ©dico?
- EstÃ¡ em rate limit?

---

### Motor de eventos

Transforma intenÃ§Ãµes em eventos persistidos.

Exemplo:

```json
{
  "type": "period_start",
  "date": "2026-04-24"
}
```

---

### Motor de cÃ¡lculo

Calcula previsÃµes e mÃ©tricas:

- PrÃ³xima menstruaÃ§Ã£o.
- Atraso.
- DuraÃ§Ã£o mÃ©dia.
- PadrÃµes de sintomas.
- HistÃ³rico.

---

### Camada de resposta

Gera uma resposta base determinÃ­stica e, se necessÃ¡rio, manda para IA humanizar.

---

# 15. Rate limit e anti-spam

Para evitar abuso e custo excessivo:

- Limitar mensagens por minuto.
- Limitar mensagens por dia.
- Detectar spam.
- Timeout temporÃ¡rio.
- NÃ£o chamar IA para toda mensagem simples.

Exemplo de regra:

```txt
5 mensagens em menos de 10 segundos = timeout temporÃ¡rio
```

Resposta:

```txt
Recebi muitas mensagens em sequÃªncia. Vou pausar por alguns minutos para evitar erros no registro. Daqui a pouco vocÃª pode continuar normalmente.
```

Para casos mais agressivos:

```txt
Detectei muitas mensagens em pouco tempo. Por seguranÃ§a, sua conversa ficarÃ¡ pausada atÃ© amanhÃ£.
```

---

# 16. MVP recomendado

## Essencial para o MVP

```txt
cadastro por WhatsApp
consentimento
inÃ­cio da menstruaÃ§Ã£o
fim da menstruaÃ§Ã£o
intensidade do fluxo
sintomas
previsÃ£o da prÃ³xima menstruaÃ§Ã£o
atraso calculado
perguntas simples sobre histÃ³rico
```

## Opcional no MVP

```txt
relaÃ§Ã£o sexual
lembretes
anticoncepcional
humor
```

## Deixar para versÃ£o 2

```txt
modo gravidez
relatÃ³rios avanÃ§ados
janela fÃ©rtil
parceiro/mÃ©dico
exportaÃ§Ã£o PDF
integraÃ§Ã£o com calendÃ¡rio
painel web avanÃ§ado
```

---

# 17. Landing page â€” posicionamento sugerido

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
Anote menstruaÃ§Ã£o, sintomas, fluxo e lembretes direto pelo WhatsApp. Simples, discreto e fÃ¡cil de manter no dia a dia.
```

## BenefÃ­cios principais

```txt
NÃ£o precisa lembrar de abrir app
Registre tudo por mensagem
Receba previsÃµes e lembretes
Consulte seu histÃ³rico quando quiser
Sem diagnÃ³sticos, sem complicaÃ§Ã£o
Privacidade e controle dos seus dados
```

## Exemplos para mostrar na LP

```txt
VocÃª: menstruei hoje
Bot: Registrei o inÃ­cio da sua menstruaÃ§Ã£o hoje âœ… Como estÃ¡ o fluxo?
```

```txt
VocÃª: tÃ´ com cÃ³lica forte
Bot: Registrei cÃ³lica forte para hoje. Espero que vocÃª fique melhor ðŸ’›
```

```txt
VocÃª: quando Ã© minha prÃ³xima menstruaÃ§Ã£o?
Bot: Pela sua mÃ©dia atual, ela estÃ¡ prevista para perto de 22/05.
```

## Aviso de seguranÃ§a na LP

```txt
Este sistema ajuda vocÃª a organizar seus registros pessoais de ciclo menstrual. Ele nÃ£o substitui orientaÃ§Ã£o mÃ©dica e nÃ£o realiza diagnÃ³sticos.
```

---

# 18. PossÃ­veis nomes de produto

Ideias iniciais:

```txt
Cicla
LunaBot
Meu Ciclo Zap
CicloZap
Lua
Lunari
Ciclo FÃ¡cil
Ciclo no Zap
MinaCiclo
Clara Ciclo
```

---

# 19. Resumo final

A ideia tem potencial porque resolve um problema simples e real:

> Aplicativos de ciclo dependem da usuÃ¡ria lembrar de abrir o app. O WhatsApp jÃ¡ faz parte da rotina.

A estratÃ©gia ideal Ã© comeÃ§ar pequeno:

1. Registrar menstruaÃ§Ã£o.
2. Registrar fim da menstruaÃ§Ã£o.
3. Registrar fluxo.
4. Registrar sintomas.
5. Calcular prÃ³xima menstruaÃ§Ã£o.
6. Responder histÃ³rico bÃ¡sico.
7. Usar IA apenas para entender mensagens naturais e humanizar respostas.

Depois, evoluir para:

- Lembretes.
- Anticoncepcional.
- Humor.
- RelaÃ§Ã£o sexual.
- Modo gravidez.
- RelatÃ³rios.
- Painel web.

O principal cuidado Ã© nÃ£o transformar o bot em mÃ©dico. O produto deve ser um **organizador pessoal de ciclo menstrual por WhatsApp**, nÃ£o uma ferramenta de diagnÃ³stico.
