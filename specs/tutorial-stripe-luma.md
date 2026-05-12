# Tutorial Stripe da Luma

Última atualização: 12/05/2026.

## Objetivo

Configurar pagamentos recorrentes da Luma com Stripe Billing e Stripe Elements, mantendo quatro preços oficiais:

- Luma Básico mensal: R$ 11,00/mês.
- Luma Básico anual: R$ 70,80/ano, equivalente a R$ 5,90/mês.
- Luma Essencial mensal: R$ 20,00/mês.
- Luma Essencial anual: R$ 118,80/ano, equivalente a R$ 9,90/mês.

Produto e preço são separados no Stripe: o produto representa `Luma Básico` ou `Luma Essencial`; cada ciclo de cobrança usa um `Price` diferente.

## Regra de Negócio

- Mensal: cobrança recorrente mensal, cancelamento agenda fim da renovação e mantém acesso até o fim do mês já pago.
- Anual: cobrança recorrente anual à vista, cancelamento agenda fim da renovação e mantém acesso até o fim do ano já pago.
- Estorno não é automático pelo painel da usuária. Deve ser tratado por suporte/Stripe Dashboard para evitar devoluções indevidas em dados sensíveis.
- Troca de plano/ciclo usa proration da Stripe (`create_prorations`), deixando a Stripe calcular créditos ou cobranças proporcionais.

## O Que Está Implementado

- Checkout integrado com Stripe Elements dentro da aplicação.
- Criação de assinatura por plano e ciclo de cobrança.
- Confirmação de pagamento.
- Webhook Stripe.
- Cancelamento ao fim do período.
- Retomada da assinatura.
- Troca de plano.
- Troca de ciclo mensal/anual.
- Troca de cartão com `SetupIntent`.
- Cartão salvo como método padrão para próximas faturas.
- Histórico de transações no perfil via invoices da Stripe.
- Sincronização local com `account_subscriptions`, incluindo `BillingInterval` e `StripePriceId`.
- Bloqueio de áudio, notificações e imagens para plano Básico.

## IDs de Teste Atuais

```env
STRIPE_BASIC_MONTHLY_PRICE_ID=price_1TWM0ALtNZgJJBvbNhv6PcpG
STRIPE_BASIC_ANNUAL_PRICE_ID=price_1TWM0BLtNZgJJBvbPCGxtjAx
STRIPE_ESSENTIAL_MONTHLY_PRICE_ID=price_1TWM0CLtNZgJJBvb650xzjlt
STRIPE_ESSENTIAL_ANNUAL_PRICE_ID=price_1TWM0DLtNZgJJBvbiMoDfF1k
```

As variáveis antigas `STRIPE_BASIC_PRICE_ID` e `STRIPE_ESSENTIAL_PRICE_ID` continuam como fallback legado, mas a aplicação nova usa prioritariamente os quatro IDs acima.

## Criar Produtos e Preços no Stripe

No Dashboard da Stripe:

1. Acesse **Product catalog**.
2. Crie o produto `Luma Básico`.
3. Adicione dois preços recorrentes em BRL:
   - R$ 11,00, mensal;
   - R$ 70,80, anual.
4. Crie o produto `Luma Essencial`.
5. Adicione dois preços recorrentes em BRL:
   - R$ 20,00, mensal;
   - R$ 118,80, anual.
6. Copie os quatro `price_...`.
7. Configure as variáveis da API.

No Render/API:

```env
Stripe__BasicMonthlyPriceId=price_live_ou_test_basico_mensal
Stripe__BasicAnnualPriceId=price_live_ou_test_basico_anual
Stripe__EssentialMonthlyPriceId=price_live_ou_test_essencial_mensal
Stripe__EssentialAnnualPriceId=price_live_ou_test_essencial_anual
```

No Docker local:

```env
STRIPE_BASIC_MONTHLY_PRICE_ID=price_do_basico_mensal
STRIPE_BASIC_ANNUAL_PRICE_ID=price_do_basico_anual
STRIPE_ESSENTIAL_MONTHLY_PRICE_ID=price_do_essencial_mensal
STRIPE_ESSENTIAL_ANNUAL_PRICE_ID=price_do_essencial_anual
```

## Chaves

Modo teste:

```env
Stripe__SecretKey=sk_test_...
Stripe__PublishableKey=pk_test_...
```

Produção:

```env
Stripe__SecretKey=sk_live_...
Stripe__PublishableKey=pk_live_...
```

Observação: a tentativa de criar Products/Prices em live via Stripe CLI falhou porque a chave live disponível no CLI era restrita (`rk_live...`) e não tinha permissão para criar catálogo. Para finalizar produção, use uma `sk_live` com permissão de Billing/Product catalog ou crie manualmente pelo Dashboard.

## Webhook

Endpoint:

```txt
POST /webhooks/stripe
```

Eventos recomendados:

- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`
- `invoice.payment_succeeded`
- `invoice.payment_failed`

Variável:

```env
Stripe__WebhookSecret=whsec_...
```

## Testar Pagamento

Use cartão de teste:

```txt
4242 4242 4242 4242
```

Dados:

- validade: qualquer data futura;
- CVC: qualquer 3 dígitos;
- nome: qualquer nome;
- CPF: um CPF válido para o formulário.

Fluxo:

1. Criar conta na web.
2. Escolher plano anual ou mensal.
3. Preencher Stripe Elements.
4. Confirmar pagamento.
5. Verificar plano e histórico de transações no perfil.
6. Verificar no Stripe se a assinatura está ativa.
7. Enviar mensagem para a Luma no WhatsApp pelo celular cadastrado.
8. Testar recurso premium, como áudio ou imagem educativa, apenas com plano Essencial.
