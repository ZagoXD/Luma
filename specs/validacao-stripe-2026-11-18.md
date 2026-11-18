# Validação de pagamentos Stripe

Data: 18/11/2026. Escopo: integração existente em modo de testes, executada com API, frontend, PostgreSQL, Redis e Cloudflare Tunnel locais. Nenhum pagamento de produção foi realizado.

## Resultado

- Backend: 224 testes aprovados, zero falhas e zero ignorados. Os testes PostgreSQL usaram um banco isolado, sem chamadas de IA pagas.
- Frontend: build de produção e ESLint aprovados.
- Checkout: pagamento efetuado pelo navegador com Payment Element e cartão de teste `4242`, sem simular o resultado da Stripe.
- Assinatura Essencial anual: cobrança de teste de R$ 118,80 e ativação no banco local confirmadas.
- Perfil inspecionado em desktop e dispositivo móvel.

## Cenários exercitados

| Cenário | Resultado |
| --- | --- |
| Conta técnica inicialmente sem plano | Nenhum plano visível |
| Checkout Básico mensal e anual, Essencial mensal e anual | Intenção de pagamento criada com credenciais de teste |
| Abrir e abandonar checkout sem pagar | Não libera plano nem adiciona fatura aberta ao histórico |
| Confirmar assinatura ainda não paga | Permanece pendente |
| Pagamento anual pelo Payment Element | Plano ativo e fatura paga de R$ 118,80 |
| Cartão recusado | Pagamento negado; plano anteriormente pago preservado |
| Pagamento confirmado na Stripe sem confirmação posterior da página | Webhook ativa o plano na Luma |
| Cancelamento ao fim do período | Renovação cancelada; acesso pago preservado |
| Retomada | Cancelamento da renovação removido |
| Alterações de plano e periodicidade | Básico anual, Básico mensal e Essencial mensal sincronizados |
| Troca de cartão | SetupIntent confirmado e cartão padrão da assinatura atualizado |
| Atualização diretamente na Stripe | Webhook público sincroniza cancelamento e retomada |
| Reenvio do evento de cancelamento | Evento aceito; acesso pago não expira indevidamente |
| Webhook com assinatura inválida | HTTP 400 |
| Checkout sem autenticação | HTTP 401 |
| Confirmação com ciclo diferente do preço realmente comprado | HTTP 400 |
| Inicialização concorrente das APIs principal e sandbox | Ambas iniciam sem deadlock no ajuste de schema |

## Correções realizadas

1. A confirmação do checkout agora valida plano e periodicidade contra o Price ID retornado pela Stripe antes de alterar os dados locais. O teste de tentativa de adulteração reproduziu HTTP 200 antes da correção e HTTP 400 depois.
2. O processamento de cancelamento preserva o acesso de assinaturas já pagas quando recebe eventos repetidos. O estado anterior `canceled` não é tratado como uma compra nunca paga.
3. A inicialização do schema é serializada por um advisory lock de sessão no PostgreSQL, compartilhado pelas instâncias da API. O lock é liberado após a inicialização ou em caso de erro.
4. No túnel `luma-local-dev`, a regra do host `dev.ia-luma.com.br` passou de `^/webhooks/twilio/whatsapp$` para `^/webhooks/(twilio/whatsapp|stripe)$`, encaminhada a `http://api-sandbox:8080`. A rota Stripe antes retornava HTML/404 do frontend; agora valida assinaturas e recebe eventos reais do ambiente de testes.

As regressões de backend foram escritas antes da implementação. Os testes de políticas de assinatura e de exclusão mútua da inicialização ficam em `whatsapp-app/tests/Luma.Tests`.

## Limites da validação

- O sucesso no modo de testes não valida cobranças reais, liquidação, estornos, disputas, regras fiscais ou permissões de escrita das credenciais de produção.
- Não houve avanço de relógio para testar uma renovação futura ou toda a sequência de retentativas de inadimplência.
- `https://api.ia-luma.com.br/health` não respondeu dentro do prazo nesta sessão. O destino de produção exige uma validação própria após configuração da hospedagem e do roteamento; ele não foi redirecionado para o ambiente de testes.
- Twilio Verify e Resend não estavam configurados no `.env` local. A conta técnica foi usada para os testes de pagamento, sem envio de SMS ou e-mail; estes fluxos não foram validados aqui.
- O `npm ci` reportou 12 vulnerabilidades nas dependências (uma baixa, sete moderadas e quatro altas). Não foram aplicadas atualizações automáticas de dependências durante esta validação; é necessária triagem antes do lançamento.
- Os segredos permanecem no `.env` ignorado pelo Git e não fazem parte deste relatório. A configuração de produção permanece separada da configuração ativa de testes.

## Limpeza e ambiente

A conta técnica foi criada com endereço no domínio reservado `example.invalid`. Após os testes, seu cliente Stripe foi removido em modo de testes, cancelando suas assinaturas, e a conta técnica local e o banco isolado foram removidos. As faturas e os eventos de teste podem permanecer no histórico da Stripe. Nenhum cadastro pessoal existente ou volume Docker foi removido.

O ambiente Docker local fica iniciado com as credenciais Stripe de testes. Os resultados não constituem uma certificação de segurança ou aprovação para pagamentos reais.
