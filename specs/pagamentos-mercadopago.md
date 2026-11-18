# Pagamentos da Luma: Mercado Pago

## Arquitetura

O módulo `whatsapp-app/src/Luma.Api/Billing` separa regras de negócio e integração externa:

- `BillingCatalog` define os quatro preços no servidor.
- `BillingPolicy` exige referência do pedido, moeda, valor, período e pagamento aprovado compatíveis.
- `ISubscriptionGateway` é a porta de saída para autorização, leitura, renovação, cartão e pagamentos.
- `BillingService` coordena pedidos, acesso, ledger de pagamentos e isolamento por conta.
- `MercadoPagoGateway` é o adaptador HTTP. Trocar o provedor exige outro adaptador e outro componente seguro de cartão, não refazer as regras de acesso.
- `BillingEndpoints` adapta HTTP para os casos de uso. JWT e titularidade são obrigatórios.
- `BillingReconciliationWorker` reconcilia cobranças periodicamente caso uma notificação não seja recebida.

O frontend mantém atomic design. `PaymentCardForm`, em organisms, encapsula o Card Payment Brick oficial. A aplicação recebe somente token opaco e e-mail do pagador; número do cartão, validade e CVV ficam nos campos do SDK. A API rejeita campos extras nos DTOs de cobrança. Não se armazenam tokens de cartão.

## Catálogo e renovação

| Plano | Mensal | Anual à vista |
|---|---:|---:|
| Básico | R$ 11,00 | R$ 70,80 |
| Essencial | R$ 20,00 | R$ 118,80 |

O anual equivale a R$ 5,90 ou R$ 9,90 por mês; não significa doze parcelas. Não foram criados produtos no painel: a assinatura sem plano associado recebe o preço validado pela API.

Cartão: autorização de recorrência via `/preapproval`, frequência de um ou doze meses. Autorização não prova pagamento. O acesso só é concedido após leitura de pagamento aprovado, com valor e moeda esperados. A cobrança inicial pode ser assíncrona; a tela informa que está aguardando confirmação.

Pix: pagamento de um período via `/v1/payments`, com QR Code/Copia e Cola e validade de 30 minutos. Não é Pix Automático, não cobra novamente e não cria uma falsa recorrência. Depois do período pago, a usuária faz nova compra.

Cancelar cartão pausa a autorização remota e interrompe renovações; o acesso pago permanece até seu vencimento. É possível retomar enquanto houver acesso. Trocas de plano são solicitadas para próximas cobranças: benefícios já pagos não são trocados nem ampliados gratuitamente. Uma mudança deve ser confirmada pelo provedor e o novo acesso só se aplica após pagamento correspondente. Não há promessa de ajuste proporcional ou parcelamento.

Reembolso não é cancelamento de renovação. Solicitações de reembolso passam pelo suporte e são efetuadas por um responsável no painel do Mercado Pago. Webhooks/reconciliação atualizam pagamentos reembolsados ou contestados e recalculam o acesso; o histórico mantém o registro da transação. Nenhuma cobrança ou devolução real deve ser feita automaticamente como teste de produção.

## Segurança e Persistência

- Abrir o checkout cria somente uma intenção local pendente, sem cobrança e sem acesso.
- Tokenização e envio ocorrem somente após a ação explícita de assinar.
- Cada intenção tem referência UUID e chave de idempotência; uma autorização pode ser recuperada pela referência após timeout.
- Lock transacional PostgreSQL por conta protege operações concorrentes entre processos; pagamentos têm ID único no ledger.
- Benefícios são calculados com pagamentos reais, não com o redirecionamento ou retorno do navegador.
- Assinaturas de outro vendedor, pagamentos de outro ambiente, pedidos de outra conta, preço adulterado e notificações sem assinatura válida são rejeitados.
- Não há fallback de assinatura ativa quando o provedor está indisponível.
- Dados pessoais continuam sujeitos à criptografia existente. Chaves privadas só ficam no `.env` ignorado e no AWS Secrets Manager.

`BillingSchema` migra os identificadores legados para campos genéricos, mantendo registros pagos e marcando o provedor anterior como `legacy`. Não é possível renovar nem ampliar esse acesso localmente. Antes da remoção da integração anterior, a consulta às APIs de teste e produção não encontrou contratos ativos. Históricos externos antigos permanecem no painel do provedor anterior, sem serem inventados no novo ledger.

## Configuração

API (ASP.NET Core, configuração por ambiente):

```dotenv
MercadoPago__PublicKey=...
MercadoPago__AccessToken=...
MercadoPago__WebhookSecret=...
MercadoPago__SellerId=...
MercadoPago__TestMode=true
Billing__ReconciliationEnabled=true
```

Docker local: equivalentes `MERCADOPAGO_PUBLIC_KEY`, `MERCADOPAGO_ACCESS_TOKEN`, `MERCADOPAGO_WEBHOOK_SECRET`, `MERCADOPAGO_SELLER_ID`, `MERCADOPAGO_TEST_MODE` em `whatsapp-app/.env`. O arquivo `.env.production` contém overrides de pagamentos reais; não deve ser usado em desenvolvimento. AWS: configurações independentes em `/luma/dev/runtime` e `/luma/production/runtime`. A chave pública é entregue em runtime pela API, sem segredo privado e sem depender de um build diferente do Next.js.

Public Key e Access Token devem pertencer ao mesmo vendedor/ambiente. Ambos podem começar por `APP_USR`: o prefixo não distingue teste de produção. A integração valida o vendedor brasileiro e a classificação de conta de testes antes de criar cobranças.

Webhooks no painel Suas integrações → aplicação → Webhooks:

- Teste: `https://dev.ia-luma.com.br/webhooks/mercadopago`.
- Produção: `https://api.ia-luma.com.br/webhooks/mercadopago`.
- Habilitar Pagamentos e Assinaturas: `payment`, `subscription_preapproval`, `subscription_authorized_payment`.
- Copiar o segredo de assinatura de cada ambiente. Sem ele, as notificações ficam bloqueadas.
- Verificação HMAC SHA-256 de `x-signature`, `x-request-id`, `data.id` e timestamp; o corpo deve apontar ao mesmo recurso assinado. O backend consulta a API antes de aplicar mudanças.

Client ID/Client Secret de OAuth não são necessários: esta integração recebe pagamentos de uma única loja, não conecta contas de terceiros.

## Testar Sem Cobrança Real

Use exclusivamente o ambiente dev e o comprador de testes criado no Mercado Pago. O cadastro de desenvolvimento não envia SMS nem e-mails reais. Use o e-mail do comprador de testes no campo do pagador se o provedor exigir uma conta de testes.

1. Crie/confirme uma conta dev e abra o checkout Básico ou Essencial, mensal ou anual.
2. Volte ao perfil sem pagar: não deve aparecer plano ativo nem número da Luma.
3. No cartão use o Mastercard de testes informado no painel: `5480 8328 0103 3311`, CVV `123`, validade `11/30`, nome `APRO` para aprovação, conforme os dados de teste atuais do provedor.
4. Confira aprovação na API e entrega do webhook; não confunda autorização recorrente com primeira cobrança aprovada.
5. Teste cancelar/retomar, troca de plano e troca de cartão. Confira os valores no painel do provedor.
6. No Pix, gerar um QR Code não ativa acesso. A confirmação exige pagamento aprovado ou simulação oficial disponível no ambiente de testes; não use seu banco real para pagar um teste.
7. Reenvie um webhook assinado: não deve gerar nova transação nem duplicar acesso.

Documentação oficial: [Card token para assinaturas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/cardtoken), [assinaturas autorizadas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/authorized-payments), [gestão](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/subscription-management) e [webhooks](https://www.mercadopago.com.br/developers/pt/docs/your-integrations/notifications/webhooks).
