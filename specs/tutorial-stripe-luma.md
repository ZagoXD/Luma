# Tutorial Stripe - Assinaturas da Luma

Este guia explica como configurar a Stripe em modo de teste para os planos da Luma e como validar o pagamento sem cobrança real.

## 1. Variáveis de ambiente

No arquivo `.env`, configure:

```env
NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY=pk_test_...
STRIPE_SECRET_KEY=sk_test_...
STRIPE_BASIC_PRICE_ID=price_...
STRIPE_ESSENTIAL_PRICE_ID=price_...
```

Use sempre chaves `test` enquanto estiver desenvolvendo. As chaves `live` só devem entrar em produção.

## 2. Criar produtos e preços na Stripe

A Stripe separa o que você vende em `Product` e quanto/como cobra em `Price`. Para a Luma, crie dois produtos com preço recorrente mensal:

1. Acesse o Dashboard da Stripe em modo de teste.
2. Vá em `Product catalog`.
3. Crie o produto `Luma Básico`.
4. Adicione um preço recorrente mensal em `BRL`, valor `5,90`.
5. Copie o ID do preço, algo como `price_...`, para `STRIPE_BASIC_PRICE_ID`.
6. Crie o produto `Luma Essencial`.
7. Adicione um preço recorrente mensal em `BRL`, valor `9,90`.
8. Copie o ID do preço para `STRIPE_ESSENTIAL_PRICE_ID`.

O backend também consegue criar preços dinamicamente se esses dois IDs não forem informados, mas para produção é melhor manter IDs fixos no `.env`.

Referência oficial: https://docs.stripe.com/invoicing/products-prices

## 3. Como o checkout funciona

O checkout é embutido na aplicação com Stripe Elements:

1. A usuária acessa `/checkout/basico` ou `/checkout/essencial`.
2. O backend cria ou reutiliza uma `Customer` da Stripe.
3. O backend cria uma assinatura com pagamento inicial pendente.
4. A página renderiza o `PaymentElement` usando o `client_secret`.
5. A Stripe confirma o pagamento no navegador.
6. O backend valida a assinatura na Stripe e ativa o plano no banco local.
7. A Luma passa a responder no WhatsApp apenas se o número tiver plano ativo ou cancelado ainda dentro do período pago.

Referência oficial do Payment Element: https://docs.stripe.com/payments/accept-a-payment

## 4. Cancelamento

No perfil da usuária, o botão `Cancelar assinatura` chama o backend.

O cancelamento é feito com `cancel_at_period_end=true`, então:

- A assinatura fica cancelada localmente.
- O acesso continua até o fim do período já pago.
- Depois da data final, a Luma deixa de responder aquele número.

Em produção, também devemos adicionar webhooks da Stripe para refletir alterações feitas fora da Luma, como falha de cobrança, reembolso, atualização manual no Dashboard ou cancelamento externo.

## 5. Como testar pagamento sem cobrança real

Use sempre chaves de teste (`pk_test` e `sk_test`) e cartões de teste da Stripe.

Cartão aprovado:

```text
Número: 4242 4242 4242 4242
Validade: qualquer data futura, por exemplo 12/34
CVC: qualquer 3 dígitos, por exemplo 123
Nome/CPF: qualquer valor válido para o formulário da Luma
```

Cartão que falha por saldo insuficiente:

```text
Número: 4000 0000 0000 9995
Validade: qualquer data futura
CVC: qualquer 3 dígitos
```

Cartão que exige autenticação 3D Secure:

```text
Número: 4000 0000 0000 3220
Validade: qualquer data futura
CVC: qualquer 3 dígitos
```

Referência oficial: https://docs.stripe.com/testing

## 6. Rodar localmente

Depois de preencher o `.env`, suba tudo com:

```powershell
docker compose up --build
```

Fluxo esperado:

1. Criar conta ou entrar.
2. Escolher plano.
3. Pagar com cartão de teste.
4. Abrir o perfil e confirmar plano ativo.
5. Enviar mensagem pelo WhatsApp usando o celular cadastrado.

