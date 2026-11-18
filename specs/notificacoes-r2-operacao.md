# Notificações e Imagens R2

## Ambientes

O worker de notificações é habilitado em produção. Dev e Docker local ficam desativados por padrão para evitar mensagens e cobranças acidentais do Sandbox. As credenciais R2 foram atualizadas e verificadas com upload, download autenticado e download pela URL pública; o objeto técnico foi apagado após o teste. Não foi necessário gerar uma imagem paga na OpenAI para essa validação.

## Regras de Elegibilidade

- Consentimento aceito e cadastro WhatsApp concluído.
- Plano Essencial com acesso vigente, incluindo assinatura cancelada com período já pago ainda válido.
- Opção correspondente habilitada pela usuária; horários independentes para previsão menstrual, pílula e check-in.
- Anticoncepcional diário somente para `pill`.
- Gravidez ativa suspende estes lembretes menstruais/contraceptivos e o check-in deste worker.
- Data de previsão e chave diária de deduplicação usam o fuso da usuária, não a data UTC.

O lembrete é apoio de organização, não recomendação de dose ou garantia de entrega. Se a aplicação ficar indisponível além da janela configurada, não dispara lembretes de pílula muito atrasados quando voltar.

## Envio e Confirmação

O processador consulta preferências a cada 60 segundos. A janela padrão é de cinco minutos após o horário escolhido. Uma chave Redis por tentativa e a restrição única do PostgreSQL impedem duplicações. O banco também faz a reserva atômica de retentativas, inclusive se o Redis estiver indisponível.

Rejeições transitórias explícitas (HTTP 429 e 5xx) permitem até três tentativas, com espera progressiva de 60 e 120 segundos, dentro da janela. Erros permanentes não são repetidos. Timeout, conexão perdida ou resposta de sucesso sem Message SID têm resultado desconhecido e não são reenviados automaticamente, pois a Twilio pode já ter aceitado a mensagem.

Estados registrados: `pending` (reserva), `submitted` (Twilio aceitou), `sent`, `delivered`, `read`, `failed` e `unknown`. Um aceite HTTP não é tratado como confirmação de entrega.

Cada mensagem recebe callback em `/webhooks/twilio/notifications/status/{deliveryId}`. O backend valida assinatura, URL pública exata, Account SID e remetente WhatsApp. Eventos repetidos ou fora de ordem não rebaixam uma entrega já confirmada. Um callback que chega antes da resposta do envio não é sobrescrito. Não é necessário cadastrar manualmente esse callback no painel: ele é informado na requisição de cada envio.

## Templates de Produção

| Tipo | Content SID | Variáveis |
| --- | --- | --- |
| Previsão amanhã | `HX4b51b08ea4f3c17dcd443c1e3071995b` | `1`: nome |
| Previsão hoje | `HX39b4b60a687825f5bb3665ca2fcb3907` | `1`: nome |
| Pílula diária | `HXa23267cf19348a4fa39f958164125141` | `1`: nome; `2`: horário local |
| Check-in | `HX459958fb7dfe7243b0eb43064e77021e` | `1`: nome |

São as versões aprovadas em `pt_BR` com acentuação preservada. Não usar os templates antigos com caracteres corrompidos.

## Configuração

```dotenv
Notifications__WorkerEnabled=true
Notifications__WorkerIntervalSeconds=60
Notifications__DueWindowMinutes=5
Notifications__MaxSendAttempts=3
Notifications__RetryDelaySeconds=60
Twilio__NotificationStatusCallbackBaseUrl=https://api.ia-luma.com.br/webhooks/twilio/notifications/status
```

Os SIDs correspondem a `Twilio__TemplatePeriodTomorrow`, `Twilio__TemplatePeriodToday`, `Twilio__TemplateContraceptiveDaily` e `Twilio__TemplateSymptomCheckin`. Credenciais e remetente usam `Twilio__AccountSid`, `Twilio__AuthToken` e `Twilio__WhatsAppFrom`.

A API recusa habilitar o worker sem configuração completa. No Docker local, as variáveis equivalentes estão em `.env.example`; deixe `NOTIFICATIONS_WORKER_ENABLED=false` salvo em teste deliberado. Na AWS, alterações de Secrets Manager só são aplicadas após recriação da API ou um novo deploy.

O schema é atualizado de forma aditiva na inicialização, acrescentando `Attempts` e `NextAttemptAt` a `notification_deliveries`, sem apagar registros existentes.

## Retenção R2

Bucket `luma`; prefixo `baby-image-generation/`. A regra `Luma baby images expire after 1 day` expira objetos após 86.400 segundos, preservando a regra anterior para abortar multipart uploads incompletos. A execução é assíncrona, não uma garantia de exclusão exatamente na hora 24. Consulte a [documentação de lifecycle do R2](https://developers.cloudflare.com/r2/buckets/object-lifecycles/).

`R2__ImageRetentionDays` documenta a retenção pretendida, mas não provisiona a regra; a expiração é executada pelo R2, não por um timer da API. Não apagar a imagem imediatamente após solicitar envio: a Twilio precisa baixá-la pela URL pública.

As chaves S3 ficam no `.env` ignorado pelo Git e nos segredos AWS. O token de gerenciamento Cloudflare não é necessário no aplicativo. Para mudar a retenção, alterar explicitamente a regra do bucket e manter a configuração/documentação alinhadas.

## Validação e Operação

Os testes automatizados cobrem datas nos dois lados da virada UTC, horários independentes, elegibilidade, deduplicação, limites de retentativa, assinatura dos callbacks e regressões de estado. Testes PostgreSQL isolados verificam concorrência entre workers e callbacks antecipados. O teste real autorizado limita-se a um lembrete para o titular; registros técnicos devem ser removidos sem tocar em cadastros reais.

Monitorar logs `Notification worker failed`, `Notification processing failed` e `submission outcome unknown`. Examinar registros `failed`/`unknown` e o Message SID no Twilio antes de qualquer reenvio manual. Não registrar texto de mensagens ou credenciais nos logs.
