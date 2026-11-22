# Preparação fiscal da Luma

Estado em 22/11/2026: estrutura implementada e testada, **emissão desativada**.
Nenhuma nota real, número fiscal ou chave de acesso foi produzido em produção.

## Fronteiras

O domínio de cobrança informa um pagamento aprovado. A preparação fiscal cria
uma pendência em `fiscal_documents`, na mesma transação local da sincronização.
O identificador do pagamento é único: repetir webhook/sincronização não duplica
a pendência. Abrir checkout, autorizar recorrência ou ter plano manual não gera
documento fiscal. O startup também prepara pagamentos históricos aprovados,
sem duplicar pendências existentes e sem emitir retroativamente.

O registro contém somente valor, moeda, data de aprovação, ID do pagamento e
estado operacional. Não copia conversas, dados de saúde ou números de cartão.
Não estamos coletando endereço fiscal adicional sem necessidade atual.

`IFiscalGateway` separa emissão, consulta e solicitação de cancelamento do futuro
provedor. O adapter atual, `DisabledFiscalGateway`, responde `not_configured`.
`FiscalProcessor` tem chave idempotente estável por pendência e reconcilia um
resultado incerto antes de tentar emitir novamente. Só é invocado explicitamente;
nenhum worker de emissão foi ativado.

## Estados

| Estado | Significado |
| --- | --- |
| awaiting_configuration | Pagamento observado; falta configuração fiscal |
| awaiting_reconciliation | Resultado incerto ou processamento externo pendente |
| authorized | Reservado para confirmação válida do emissor, com número e chave |
| rejected | Rejeição que exige análise, não reenvio cego |
| review_required | Divergência, resposta incompleta ou pagamento revertido |

Retentativa: `Attempts`, `NextAttemptAt` e erro técnico sem dados sensíveis.
O processor não trata HTTP timeout como rejeição definitiva. Uma resposta
`authorized` sem número ou chave não é aceita. Valores sintéticos de autorização
existem somente nos testes com gateway fake.

Reembolso, chargeback ou reversão financeira marcam revisão fiscal. Cancelar uma
assinatura não apaga um pagamento anterior nem cancela automaticamente uma nota.
Reembolso legal e cancelamento fiscal devem ter processos independentes.

## Ativação futura

Manter `FISCAL_EMISSION_ENABLED=false` (`Fiscal__EmissionEnabled=false` na API).
Nesta etapa, `true` recusa o startup para impedir que alguém confunda preparação
com emissão configurada. Não basta alterar essa flag para emitir notas.

Antes da integração real, serão necessários:

1. Validar com a contabilidade o documento adequado para a atividade; em geral,
   discutir NFS-e para prestação de serviços, não pressupor NF-e de mercadorias.
2. Confirmar município, inscrição, regime, classificação/tributação do serviço e
   regras de competência, inclusive pagamento anual antecipado e reembolso.
3. Definir API nacional, municipal ou intermediário compatível; implementar um
   adapter de `IFiscalGateway`, sem levar HTTP/certificados para BillingService.
4. Completar o DTO fiscal com os dados obrigatórios do tomador e do serviço,
   validados e obtidos no backend. Dados pessoais adicionais devem ser protegidos
   pela criptografia existente e ter política de retenção explícita.
5. Configurar certificado/credenciais no gerenciador de segredos, nunca no Git,
   frontend, conversa com a IA ou logs.
6. Homologar emissão, consulta, timeout após autorização, rejeição e cancelamento;
   verificar a autenticidade do resultado conforme o contrato do emissor.
7. Implementar o worker/operador autorizado, observabilidade e entrega/consulta de
   documento fiscal no painel. Comprovante Mercado Pago não deve se chamar nota.
8. Definir retenção fiscal e privacidade na exclusão da conta, sem reativar acesso.
9. Substituir a trava de ativação somente após essas validações, com implantação
   controlada. Pendências antigas exigem decisão contábil; não emitir em massa.

Documentação oficial para a escolha futura:
[APIs NFS-e de homologação e produção](https://www.gov.br/nfse/pt-br/biblioteca/documentacao-tecnica/apis-prod-restrita-e-producao).

## Testes

- Pagamento pendente não gera nota; aprovado gera uma pendência idempotente.
- Emissor desativado não autoriza documento.
- Falha de transporte leva à reconciliação, sem segunda emissão cega.
- Reversão de pagamento exige revisão, sem cancelamento fiscal automático.
- Emissão continua desligada em `.env`, `.env.production` e no Compose.

A estrutura é uma preparação extensível, não uma integração fiscal homologada.
