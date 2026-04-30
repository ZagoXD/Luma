# Tutorial Stripe da Luma

Última atualização: 30/04/2026.

## Objetivo

Configurar pagamentos recorrentes da Luma com Stripe Billing e Stripe Elements, começando pelo modo de testes.

## Planos

- Luma Básico: R$ 5,90/mês.
- Luma Essencial: R$ 9,90/mês.

## O Que Já Está Implementado

- Checkout integrado na aplicação.
- Stripe Elements embutido na página.
- Criação de assinatura.
- Confirmação de pagamento.
- Webhook Stripe.
- Cancelamento ao fim do período.
- Retomada da assinatura.
- Troca de plano.
- Troca de cartão.
- Salvamento do cartão como método padrão da assinatura.
- Sincronização local de assinatura com `account_subscriptions`.

## Criar Produtos e Preços no Stripe

No Dashboard da Stripe em modo de teste:

1. Acesse **Product catalog**.
2. Clique em **Add product**.
3. Crie o produto `Luma Básico`.
4. Adicione preço recorrente mensal:
   - moeda: BRL;
   - valor: 5,90;
   - intervalo: mensal.
5. Copie o `price_...`.
6. Repita para `Luma Essencial` com R$ 9,90/mês.

Variáveis da API:

```env
Stripe__BasicPriceId=price_do_basico
Stripe__EssentialPriceId=price_do_essencial
```

No Docker local, via `.env`:

```env
STRIPE_BASIC_PRICE_ID=price_do_basico
STRIPE_ESSENTIAL_PRICE_ID=price_do_essencial
```

## Chaves

Modo teste:

```env
Stripe__SecretKey=sk_test_...
Stripe__PublishableKey=pk_test_...
```

No Docker local:

```env
STRIPE_SECRET_KEY=sk_test_...
NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY=pk_test_...
```

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

No Docker local:

```env
STRIPE_WEBHOOK_SECRET=whsec_...
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
2. Escolher plano.
3. Preencher Stripe Elements.
4. Confirmar pagamento.
5. Verificar perfil.
6. Verificar no Stripe se a assinatura está ativa.
7. Enviar mensagem para a Luma no WhatsApp pelo celular cadastrado.

## Cancelamento

O cancelamento usa `CancelAtPeriodEnd=true`.

Comportamento esperado:

- Na Luma, o status fica como cancelado.
- O acesso continua até `CurrentPeriodEndsAt`.
- A usuária pode retomar antes do fim do período.

## Troca de Plano

A troca usa atualização de item da assinatura no Stripe com proration.

Comportamento:

- Básico pode migrar para Essencial.
- Essencial pode migrar para Básico.
- A Stripe calcula ajustes proporcionais quando aplicável.

## Troca de Cartão

Implementado com `SetupIntent`.

Fluxo:

1. Usuária abre perfil.
2. Clica em trocar cartão.
3. Preenche Stripe Elements.
4. Stripe salva método de pagamento.
5. API define o cartão como padrão para próximas faturas.

## Produção

Para produção:

1. Criar produtos e preços novamente no modo produção.
2. Trocar `sk_test` por `sk_live`.
3. Trocar `pk_test` por `pk_live`.
4. Criar webhook de produção.
5. Configurar `Stripe__WebhookSecret` de produção.
6. Testar com compra real de baixo valor antes de abrir para usuárias.
