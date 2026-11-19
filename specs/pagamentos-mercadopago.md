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

Cartão: autorização de recorrência via `/preapproval`, frequência de um ou doze meses. Autorização não prova pagamento. O acesso só é concedido após leitura de pagamento aprovado, com valor e moeda esperados. A primeira cobrança é assíncrona e, segundo a documentação do provedor, ocorre aproximadamente uma hora depois da assinatura; a tela informa que está aguardando confirmação, o webhook e a reconciliação podem concluir o acesso mesmo após a usuária fechar a página.

Pix foi retirado desta versão. A API aceita somente token opaco de cartão; não há QR Code nem compra avulsa nesse checkout.

Desativar a renovação pausa a autorização remota e mantém o acesso pago até o vencimento; a renovação pode ser retomada enquanto houver acesso válido. Cancelar imediatamente usa `status=cancelled` no provedor e um estado terminal local, que webhooks não podem reativar. Também interrompe trocas agendadas. Trocas de plano só alteram benefícios depois do pagamento correspondente; não há promessa de parcelamento ou ajuste proporcional.

Reembolso é separado de cancelamento e renovação. Configurações → Arrependimento e reembolso permite solicitar a devolução integral dentro de sete dias do primeiro pagamento aprovado desta contratação. Pedidos são gravados antes da chamada externa; o prazo é preservado nas retentativas. O adaptador usa `/v1/payments/{id}/refunds` com chave estável de idempotência e valida valor integral e aprovação. O worker retoma operações pendentes, sem duplicar estornos. Depois do prazo automático, o suporte continua disponível para outros direitos. Não se desconta tarifa da cliente no arrependimento; eventual tarifa não restituída pelo provedor é custo da Luma. Não realizar cobranças ou reembolsos reais como testes de produção.

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

Em dev, `MERCADOPAGO_TEST_BUYER_EMAIL` (`MercadoPago__TestBuyerEmail` na API) pode preencher o Brick com o e-mail oficial de um comprador de testes. A configuração é ignorada em produção. O cadastro da Luma continua usando o e-mail escolhido pela usuária; a identidade do pagador de sandbox é separada e não é uma credencial de acesso à conta.

Webhooks no painel Suas integrações → aplicação → Webhooks:

- Teste: `https://dev.ia-luma.com.br/webhooks/mercadopago`.
- Produção: `https://api.ia-luma.com.br/webhooks/mercadopago`.
- Habilitar Pagamentos e Assinaturas: `payment`, `subscription_preapproval`, `subscription_authorized_payment`.
- Copiar o segredo de assinatura de cada ambiente. Sem ele, as notificações ficam bloqueadas.
- Verificação HMAC SHA-256 de `x-signature`, `x-request-id`, `data.id` e timestamp; o corpo deve apontar ao mesmo recurso assinado. O backend consulta a API antes de aplicar mudanças.

Client ID/Client Secret de OAuth não são necessários: esta integração recebe pagamentos de uma única loja, não conecta contas de terceiros.

### Particularidades do Sandbox e Pendências

O token `APP_USR` de testes de **Assinaturas** pertence ao vendedor de testes. Nos ensaios reais de 19/11/2026, tokenização e autorização retornaram HTTP 201 com o e-mail retornado pela API ao criar um comprador de testes. Um e-mail arbitrário de domínio comum causou `Both payer and collector must be real or test users`; inventar um endereço com o ID do comprador também não funcionou. Nome `TESTUSER...` não substitui o e-mail. As autorizações isoladas de diagnóstico foram canceladas, sem cobrança real.

A consulta real de faturas revelou `Invalid value for limit` para páginas de 100 e de 20. O adaptador usa dez, aceito pelo endpoint, com teste de regressão. A busca de assinatura usa `q` e exige igualdade da referência no resultado; `external_reference` era ignorado como filtro pelo endpoint de pesquisa de assinaturas.

Validação real em dev em 19/11/2026: primeira cobrança aprovada e registrada no PostgreSQL; renovação desligada preservando o período pago e retomada; troca de plano sem antecipar benefícios; troca de cartão; migração mensal/anual agendada e seu cancelamento; bloqueio de consulta a pedido de outra conta; cancelamento imediato terminal, sem reativação por refresh; exclusão por senha com cancelamento remoto e rejeição da sessão antiga. Datas do provedor com offset são normalizadas para UTC antes de persistir no PostgreSQL.

**Pendência externa de reembolso:** a chamada real de sandbox a `POST /v1/payments/{id}/refunds` retornou `401 Unauthorized use of live credentials`, embora compra, consulta e cancelamento funcionem com o mesmo vendedor de testes. O pedido de arrependimento permanece registrado e não é marcado como concluído. É necessário confirmar a permissão/compatibilidade do token de testes com esse endpoint junto ao Mercado Pago e repetir o ensaio. Não foi usado token de produção para contornar o bloqueio e não se afirma que o estorno real passou. Reembolsos integrais e suas retentativas estão cobertos por testes automatizados; a devolução das tarifas do vendedor continua dependendo da política comercial do provedor.

Trocar a frequência mensal/anual não usa um update não suportado pelo provedor. O checkout solicita nova autorização com início na data final do período já pago e valida essa data no retorno. Após confirmar a nova autorização, a renovação antiga é pausada. O perfil mostra a troca agendada e permite cancelá-la; a renovação anterior permanece desativada até a usuária retomá-la explicitamente. Para segurança, a troca não é iniciada nas duas horas anteriores ao vencimento.

## Testar Sem Cobrança Real

Use exclusivamente o ambiente dev e o comprador de testes criado no Mercado Pago. O cadastro de desenvolvimento não envia SMS nem e-mails reais. Use o e-mail do comprador de testes no campo do pagador se o provedor exigir uma conta de testes.

1. Crie/confirme uma conta dev e abra o checkout Básico ou Essencial, mensal ou anual.
2. Volte ao perfil sem pagar: não deve aparecer plano ativo nem número da Luma.
3. No cartão use o Mastercard de testes informado no painel: `5480 8328 0103 3311`, CVV `123`, validade `11/30`, nome `APRO` para aprovação, conforme os dados de teste atuais do provedor.
4. Confira aprovação na API e entrega do webhook; não confunda autorização recorrente com primeira cobrança aprovada.
5. Teste cancelar/retomar, troca de plano e troca de cartão. Confira os valores no painel do provedor.
6. Teste renovação desligada, cancelamento imediato e arrependimento como operações distintas; valide também pedido expirado, reenvio idempotente e indisponibilidade externa.
7. Reenvie um webhook assinado: não deve gerar nova transação nem duplicar acesso.

Documentação oficial: [Card token para assinaturas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/cardtoken), [assinaturas autorizadas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/authorized-payments), [gestão](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/subscription-management) e [webhooks](https://www.mercadopago.com.br/developers/pt/docs/your-integrations/notifications/webhooks).
