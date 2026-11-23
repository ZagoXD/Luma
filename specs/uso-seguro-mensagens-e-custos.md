# Uso seguro, mensagens compostas e custo

Implementação de 22/11/2026. Configurações padrão documentadas abaixo; a API é
a autoridade para verificar conta, plano, consentimento e limites.

## Política de uso

| Recurso | Básico | Essencial |
| --- | --- | --- |
| Mensagens admitidas para IA por dia | 10 | 20 |
| Mensagens admitidas para IA por mês | 150 | 300 |
| Cadastro inicial | Até 12/dia, também contabilizadas no mês | Até 12/dia, também contabilizadas no mês |
| Transcrição | Não disponível | 600 segundos/mês |
| Tamanho de cada áudio | Não disponível | 60 segundos, até 10 MB |
| Solicitações de imagem | Não disponível | 2/mês |

O dia renova à meia-noite em `America/Sao_Paulo`, não 24 horas após a primeira
mensagem. A cota mensal de IA e áudio/imagens renovam no início do mês nesse fuso, inclusive em contratos anuais. O fuso do lembrete
continua sendo individual e não muda a janela de contabilização do plano.

Uma mensagem composta é uma admissão, não uma por ferramenta ou chamada interna.
Um áudio usa uma admissão diária, uma mensal e os segundos efetivos, arredondados para cima.
A reserva dessas três franquias é atômica. Bloquear uma delas não gasta as outras.
Tentativas admitidas com falha posterior podem consumir a cota: um timeout não
comprova ausência de cobrança do provedor. Reentregas com o mesmo MessageSid não
disparam outra chamada paga, mesmo se a tentativa anterior terminou de forma incerta.

Os contadores ficam no PostgreSQL por conta, não no navegador, telefone ou memória
do container. Mudança de número, reinício e troca de plano não zeram o consumo.
Advisory locks por conta impedem exceder a cota em processos concorrentes. O
controle Redis de rajada, cooldown e trava permanece separado da franquia diária.

## Caminho sem IA

Depois do cadastro e consentimento, enviar `registrar`, `menu` ou `registros`
abre um fluxo determinístico persistido e criptografado por até 24 horas:

1. Início da menstruação.
2. Fim da menstruação.
3. Fluxo.
4. Sintoma.
5. Relação sexual.
6. Humor.
7. Correção do início da última menstruação.
8. Notificações do Essencial, incluindo horários e fuso.

Datas aceitas: hoje, ontem, anteontem e DD/MM/AAAA, até dois anos atrás. Datas
futuras e correções conflitantes são recusadas. A correção altera o ciclo e seu
evento, sem criar outro ciclo. As previsões são recalculadas com a referência salva.
`cancelar` fecha somente o menu ativo; não cancela a assinatura.

Para a pílula, o menu exige método já cadastrado ou confirmação explícita antes
de ativar o lembrete. Nenhum método é inferido dessa escolha. Configuração e
envio efetivo continuam sendo processos distintos.

`minha conta`, `meu calendário`, `suporte`, `privacidade`, `notificações` e
`meus lembretes`, `meu uso` e `meus limites` têm respostas locais. O MCP também
expõe get_usage_summary, com os contadores reais da conta, sem inferi-los do chat.
Perfil, calendário, suporte e configuração
web não dependem da cota de conversa. Os controles contra abuso continuam ativos.

## Entrada de WhatsApp

- Validar assinatura, URL, conta, destinatário e MessageSid antes de processar.
- Detectar grupos por sinais específicos; ConversationSid sozinho não é um grupo.
- Em grupos, não ler/escrever histórico de saúde nem chamar IA. Avisar no máximo
  uma vez por remetente a cada dia e ignorar as demais mensagens.
- O produto Twilio WhatsApp não oferece participação/saída de grupos nativos.
  Este código é um bloqueio defensivo, não uma promessa de sair via API.
- Imagens, vídeos, documentos, contatos, stickers e localização têm resposta fixa.
- Anexo com legenda também é recusado: pedir texto separado, sem fingir ter visto
  a imagem e sem registrar uma legenda fora de contexto.
- Aceitar somente um áudio sem legenda; conferir o conteúdo baixado e sua duração
  antes de enviar ao serviço de transcrição. FFmpeg decodifica amostras, redefine
  timestamps e produz PCM limitado, sem arquivos temporários. Só esse áudio
  normalizado é enviado; metadados de duração adulterados não liberam um arquivo
  longo. TagLibSharp lê a duração do WAV normalizado. O decoder não recebe
  credenciais, não pode acessar rede/arquivos por protocolos e tem timeout de
  dez segundos. Há no máximo dois processamentos de áudio simultâneos por API.
- O download exige HTTPS e URL de mídia da própria conta Twilio, com limite de
  leitura em streaming. Credenciais não são enviadas a URLs arbitrárias.
- Assuntos obviamente externos são recusados localmente. Casos ambíguos vão ao
  modelo, com ferramenta out_of_scope e proibição de assistência genérica.
- Rotas `/dev/*` ficam desligadas por padrão. Só respondem em Development por
  loopback com `Development__AccessToken` de pelo menos 32 caracteres e o mesmo
  valor no header `X-Luma-Development-Key`. Não podem ser uma
  entrada pública para se passar por um número de WhatsApp sem assinatura.

Referências técnicas: [grupos Twilio](https://help.twilio.com/articles/56973301485979-Can-WhatsApp-Business-Senders-WABA-Create-Native-WhatsApp-Groups)
e [mídias recebidas](https://www.twilio.com/docs/whatsapp/tutorial/send-and-receive-media-messages-whatsapp-csharp-aspnet).

## Mensagens compostas e otimizações

`record_actions` aceita até três registros explícitos de menstruação, fluxo,
sintoma, humor ou relação. O backend valida o lote antes de executá-lo e usa a
transação do atendimento. Se uma ação posterior falhar, as anteriores são
revertidas; nenhuma confirmação é inventada pelo modelo.

Para combinações óbvias, uma proteção adicional detecta interpretações parciais:
o modelo recebe o erro de planejamento e pode reformular a lista, sem executar
só o primeiro registro. Isso não é uma garantia de compreensão perfeita de toda
linguagem natural; ambiguidades continuam exigindo esclarecimento.

Confirmações de gravação são retornadas diretamente do backend: uma chamada ao
modelo, em vez de uma segunda apenas para reescrever a confirmação. Consultas que
misturam dados pessoais e conhecimento podem continuar usando várias chamadas.
O histórico tem no máximo quatro pares de mensagens, com até 600 caracteres por
lado. Ferramentas irrelevantes à etapa do cadastro são omitidas do request.

`usage_calls` registra provedor, modelo, unidades e estado, nunca o texto de saúde.
Tokens de entrada, cache e saída da Responses API usam o usage retornado pelo
provedor. `ReferenceCostUsd` é estimativa tarifária, não a fatura real. Embeddings,
áudio e imagens têm registros separados; custo desconhecido fica nulo, não é
anunciado como gratuito. Um registro `reserved` pode representar resultado incerto.

Há uma franquia mensal explícita de IA, exibida antes da compra, no painel e pelo MCP.
Não há teto monetário mensal oculto. O menu de registros e as consultas determinísticas continuam disponíveis.
As franquias limitam abuso, mas não garantem margem positiva em qualquer padrão
de uso. Custos Twilio, infraestrutura, câmbio, impostos e taxas devem ser medidos.

## Preços e validação

Preços finais para novas contratações: Básico R$ 32,90/mês ou R$ 334,80/ano;
Essencial R$ 59,90/mês ou R$ 658,80/ano. Anual integralmente à vista, sem parcelamento;
equivalentes R$ 27,90 e R$ 54,90/mês. `ContractAmount` e `PendingContractAmount`
preservam preços contratados e trocas previamente autorizadas. A migração aditiva
preenche contratos legados com os preços anteriores, sem alterar cobranças no provedor.
O painel usa o preço real do contrato. Uso mensal considera recibos anteriores à
introdução da nova franquia, sem zerar consumo por reinício, novo telefone ou downgrade.
O endpoint de uso informa `monthlyLimit`, `monthlyUsed`, `dailyResetsAt` e `monthlyResetsAt`.
O painel mostra barras e atualiza contadores; `meu uso` funciona sem IA mesmo após esgotamento.
Novos defaults estão no `.env`, `.env.production` e `.env.example`: `USAGE_BASIC_MONTHLY_MESSAGES=150`
e `USAGE_ESSENTIAL_MONTHLY_MESSAGES=300`; Docker mapeia para `Usage__*`. Na AWS os defaults
do código se aplicam também quando as variáveis não existem no segredo; geração de vetores
permanece desativada e a release importa somente os dois novos vetores de produto preparados.

Sonda offline do orquestrador, com HTTP simulado e tokenização local aproximada:

| Cenário | Chamadas ao modelo |
| --- | --- |
| Registro simples | 1 (antes: 2) |
| Dois registros válidos no mesmo lote | 1 |
| Consulta pessoal | 2 |
| Consulta RAG | 2 |
| Consulta pessoal + RAG | 3 |

No mix hipotético usado na análise, o custo conservador passou de cerca de
US$ 0,00823 para US$ 0,00634 por mensagem (~23% menor). A simulação adiciona 15%
à estimativa de entrada e considera pelo menos 200 tokens de saída por chamada;
não desconta atalhos locais. Com câmbio **hipotético** de R$ 6/US$, 50 mensagens
custariam aproximadamente R$ 1,90 e 300, R$ 11,41, apenas em texto. Não são
projeções de margem nem medição de tráfego real.

Um teste real com dados fictícios confirmou os dois registros e as datas em uma
chamada: 2.693 tokens de entrada e 65 de saída. Pela tarifa de referência consultada
do gpt-5.4-mini, aproximadamente US$ 0,0023. Os demais testes não enviam mensagens
reais nem transcrevem/geram imagens pagas.

O modelo antigo gpt-image-1 foi encerrado pelo provedor. Os novos defaults e os
arquivos .env usam gpt-image-2.6-flare com qualidade low. Uma configuração antiga
gpt-image-1 também recebe esse fallback com aviso no log; atualizar os segredos
AWS no próximo deploy para não depender dessa compatibilidade.
[Modelo de texto](https://developers.openai.com/api/docs/models/gpt-5.4-mini),
[modelo de imagem](https://developers.openai.com/api/docs/models/gpt-image-2.6-flare),
[descontinuações](https://developers.openai.com/api/docs/deprecations).

## Configuração e publicação

Os arquivos `.env`, `.env.production` e `.env.example` receberam:

```dotenv
USAGE_BASIC_DAILY_MESSAGES=10
USAGE_ESSENTIAL_DAILY_MESSAGES=20
USAGE_ONBOARDING_DAILY_MESSAGES=12
USAGE_MONTHLY_AUDIO_SECONDS=600
USAGE_MAX_AUDIO_SECONDS=60
USAGE_MONTHLY_IMAGES=2
USAGE_TIME_ZONE=America/Sao_Paulo
FISCAL_EMISSION_ENABLED=false
OPENAI_IMAGE_MODEL=gpt-image-2.6-flare
OPENAI_IMAGE_QUALITY=low
LUMA_MESSAGE_LOCK_SECONDS=90
```

Compose converte para `Usage__BasicDailyMessages`, `Usage__EssentialDailyMessages`,
`Usage__OnboardingDailyMessages`, `Usage__MonthlyAudioSeconds`,
`Usage__MaxAudioSeconds`, `Usage__MonthlyImages`, `Usage__TimeZone`,
`Fiscal__EmissionEnabled`, `OpenAI__ImageModel`, `OpenAI__ImageQuality` e
`Luma__MessageLockSeconds`. Nenhum segredo externo novo é necessário.

O Docker instala FFmpeg e executa a API sem root. Fora do Docker, instalar FFmpeg
no PATH ou definir `Audio__FfmpegPath` para um executável confiável. Nos testes
nativos, `LUMA_FFMPEG` pode apontar para esse executável. A CI instala o decoder
antes de executar os testes. Não há normalização por serviço pago.

Validação local: suíte completa de backend com PostgreSQL e decoder reais,
testes do frontend, lint/build, testes do worker de lista de espera e navegador
em 1280 e 375 pixels. O container também iniciou com banco vazio, raiz somente
leitura e usuário 1654; `/account/usage` sem autenticação retornou 401 e a entrada
pública `/dev/messages` retornou 404. Nenhuma credencial de API paga foi fornecida
a esse container de validação.

O startup cria as tabelas novas de maneira aditiva. Não resetar bancos ou volumes.
Publicar API e frontend juntos por causa dos limites e da versão dos termos
2026-11-22. Atualizar os segredos/runtime AWS junto das imagens; `.env` local não
atualiza automaticamente o Secrets Manager. Não religar a AWS apenas para essa
preparação sem decidir sobre o consumo de créditos.
