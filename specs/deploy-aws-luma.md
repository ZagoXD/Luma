# Operação da Luma na AWS

## Ambientes e custos

A implantação usa o plano FREE do projeto AWS, com consumo dos créditos disponíveis, sem upgrade automático. A região é `us-east-2`. Free plan não significa que EC2, disco, IPv4, backups ou Secrets Manager não consumam créditos. A expiração do plano ou dos créditos pode interromper o ambiente. Não foram contratados Savings Plans ou reservas.

Uma EC2 `t3.small` executa dev e produção para conter o custo inicial. CPU usa créditos no modo Standard, sem cobrança de CPU Unlimited. O disco gp3 de 30 GB e o swap são criptografados pelo EBS. Esta configuração não oferece alta disponibilidade: uma falha da instância afeta os dois ambientes.

Cada ambiente tem seus próprios containers de API, web, PostgreSQL com pgvector, Redis, volumes, rede de dados, JWT e segredos. PostgreSQL e Redis não publicam portas. A instância também não tem portas de entrada abertas: administração por SSM, tráfego HTTPS por Cloudflare Tunnel.

| Ambiente | Site e webhook WhatsApp | Stripe | Número WhatsApp |
| --- | --- | --- | --- |
| Dev | `https://dev.ia-luma.com.br` | Testes | Sandbox `+14155238886` |
| Produção | `https://ia-luma.com.br` | Live | `+16204008668` |

A API de produção usa `https://api.ia-luma.com.br`. O webhook Stripe é `/webhooks/stripe`; o webhook Twilio é `/webhooks/twilio/whatsapp`. No host dev, `/health` consulta a API e `/deployment` informa o commit instalado, sem credenciais. A mesma convenção existe no site de produção.

## Preservação do ambiente local

A branch `backup/local-cloudflared` preserva o ambiente local anterior à migração e foi enviada ao GitHub. Os volumes locais PostgreSQL/Redis não são apagados. Produção recebe uma cópia dos dados locais, preservando as chaves de privacidade necessárias para decifrá-los. Dev recebe apenas o schema e o acervo RAG vetorizado, sem cadastros ou históricos pessoais.

O túnel local `luma-local-dev` permanece disponível; o túnel AWS é separado (`luma-aws`). Voltar ao computador exige atualizar os registros DNS para o túnel local, iniciar os containers locais e conferir os webhooks. Não se deve conectar o computador ao token do túnel AWS: isso distribuiria requisições entre origens diferentes.

Os dados que forem gravados na AWS depois da migração não retornam automaticamente ao volume local. Antes de desligar a AWS definitivamente, exporte os bancos e segredos de privacidade atuais, valide a restauração e somente então altere o DNS. A branch preserva código, não faz backup do banco.

## Infraestrutura e segredos

- Stack CloudFormation: `luma-host`; template em `infra/aws/stack.json`.
- Instância inicial: `i-07a4a9e692e4a5c55`.
- Bucket privado: `luma-private-162956754501-us-east-2`.
- Secrets Manager: `/luma/dev/runtime`, `/luma/production/runtime` e `/luma/edge/runtime`.
- Deploy e backup: `/opt/luma/infra/aws/deploy.py`.
- Arquivos de configuração gerados: `/opt/luma/runtime/<ambiente>/`, somente root.
- Chaves públicas de assinatura: `infra/aws/keys/`. Não são segredos.

`prepare-config.cjs` usa a interpretação do Docker Compose para ler a configuração local, gera isolamento e prepara JSONs privados em `.backups/`. Ele se recusa a sobrescrever chaves já geradas. Não execute o gerador novamente para uma base já migrada: trocar as chaves de privacidade sem uma migração adequada torna os dados ilegíveis.

As credenciais OpenAI podem ser iguais nos dois ambientes; a memória conversacional e os dados pessoais não são compartilhados. O RAG é copiado com os vetores existentes. `Rag__AllowEmbeddingGeneration=false` impede geração automática de embeddings na inicialização; se o acervo mudar, deve existir um processo explícito de atualização antes do deploy.

Notificações ficam desativadas até recuperar as credenciais e validar os templates. A configuração da Stripe é separada por ambiente; nenhuma chave live é aceita em dev pelo serviço de deploy.

## CI e deploy

1. Push na `main` executa o CI: backend, integração PostgreSQL, lint/build do frontend e testes de segurança do deploy.
2. O workflow `Deploy AWS` só continua automaticamente quando o CI de um push da própria `main` termina com sucesso. Pull requests não recebem os segredos de deploy.
3. O GitHub constrói as imagens uma vez e publica arquivos imutáveis na release `aws-<SHA>`.
4. Uma assinatura Ed25519 protege o manifesto do canal dev. A instância verifica a assinatura, os nomes e hashes dos arquivos antes de carregar as imagens.
5. O serviço instala a versão, verifica API e web e publica `/deployment` apenas depois de saudável. O workflow aguarda o commit correto e o health check.
6. Para produção, abra **Actions → Deploy AWS → Run workflow → environment: production**. Sem SHA informado, promove o commit atual de dev. Um SHA informado deve coincidir com o dev validado. As imagens não são reconstruídas.

A organização AWS deste projeto bloqueou o gerenciamento de provedores IAM OIDC. Por isso não foram colocadas Access Keys AWS de longa duração no GitHub nem instalado um runner com acesso aos bancos. A AWS consulta releases assinadas do repositório público; as únicas chaves privadas de assinatura ficam nos GitHub Environments `dev` e `production`, restritos a branches protegidas. Manter o repositório público é requisito desta implementação.

As releases de imagens não contêm `.env` ou segredos. O status de um workflow não deve ser interpretado como sucesso enquanto o passo de verificação de saúde não terminar.

## Backup, rollback e manutenção

Há backup diário dos bancos e backup antes de cada atualização. São dumps em bucket privado, com criptografia SSE-S3 e retenção de sete dias em `backups/`. O disco e os segredos usam retenção na exclusão da stack para evitar perda acidental; a exclusão da stack não elimina todos os custos dos recursos retidos.

O deploy conserva a versão atual e a anterior de cada ambiente e tenta retornar à imagem anterior se os health checks falharem. Isso não desfaz alterações do schema: mudanças incompatíveis exigem restauração explícita do backup. O histórico de releases do GitHub deve preservar as versões usadas em dev, produção e rollback.

Com AWS CLI autenticada no perfil `luma`, o helper `infra/aws/invoke-command.ps1` permite comandos administrativos pelo SSM. Nunca use comandos que exibam variáveis de ambiente, conteúdo dos segredos ou dados pessoais nos logs.

```powershell
./infra/aws/invoke-command.ps1 -Commands @('docker ps --format "{{.Names}} {{.Status}}"')
./infra/aws/invoke-command.ps1 -Commands @('systemctl status luma-deploy.timer --no-pager')
./infra/aws/invoke-command.ps1 -Commands @('journalctl -u luma-deploy.service -n 20 --no-pager')
./infra/aws/invoke-command.ps1 -Commands @('systemctl start luma-backup.service')
```

Para renovar credenciais, edite o JSON do segredo correspondente no Secrets Manager, mantendo as chaves de privacidade se não estiver fazendo rotação planejada. Um novo deploy carregará a versão atual do segredo. Não edite apenas o `.env` local esperando alterar a AWS.

Para conter consumo, é possível parar a instância após realizar e verificar os backups. EBS, Secrets Manager e armazenamento S3 podem continuar consumindo créditos. Não faça upgrade para Paid para reativar o ambiente sem uma decisão explícita do responsável. A desativação de proteção contra término e a exclusão de bancos ou volumes exigem uma decisão deliberada.

## Credenciais pendentes

Por decisão do responsável, estes recursos ficam configuráveis, mas sem validação real nesta migração:

- **ElevenLabs:** Developers → API Keys. Recuperar a API key com permissão de Speech to Text. Campos de API: `ElevenLabs__ApiKey`, modelo `ElevenLabs__SpeechToTextModel`.
- **Resend:** API Keys para a chave de envio; Domains para o domínio verificado; Templates para os IDs/aliases publicados de boas-vindas, assinatura, recuperação de senha e suporte. Campos `Resend__ApiKey` e `Email__Templates__*`, além de remetente e destinatário do suporte.
- **Cloudflare R2:** R2 → Manage R2 API Tokens → Account API Tokens; permissão Object Read & Write limitada ao bucket `luma`. Recuperar Access Key ID e Secret Access Key, não o token da API Cloudflare. Campos `R2__AccessKeyId`, `R2__SecretAccessKey`, endpoint e domínio público de mídia.
- **Twilio Verify:** Console → Verify → Services → serviço da Luma → Service SID (`VA…`). Campo `Twilio__VerifyServiceSid`; canal `sms`.
- **Notificações:** Messaging → Content Template Builder; recuperar os Content SIDs (`HX…`) aprovados e preencher `Twilio__TemplatePeriodTomorrow`, `Twilio__TemplatePeriodToday`, `Twilio__TemplateContraceptiveDaily` e `Twilio__TemplateSymptomCheckin`. Só então validar e ativar `Notifications__WorkerEnabled`.

Não foram feitas chamadas pagas de teste de OpenAI, Twilio ou ElevenLabs. A migração não substitui a revisão das dependências npm, dos termos de uso, da política de privacidade ou dos fluxos fiscais, estornos e disputas antes de um lançamento comercial amplo.
