# Conta, privacidade e lista de espera

Atualização: 19/11/2026. Documentos jurídicos publicados com a versão imutável `2026-11-18`, revisados e aprovados pelo responsável em 19/11/2026. Essa aprovação não equivale a uma certificação jurídica independente. Não há nova pendência de revisão de textos nesta etapa.

## Cadastro e documentos

`/termos` e `/privacidade` são públicos. O rodapé, cadastro e Configurações do perfil oferecem links. O cadastro exige aceite dos termos e consentimento específico de saúde, desmarcados inicialmente; o backend rejeita versão desatualizada ou aceite ausente e grava versão e data. CPF do responsável não é publicado; identificação empresarial, CNPJ, endereço e contato foram fornecidos pelo titular.

## Assinatura

- Somente cartão de crédito: mensal ou anual à vista.
- Renovação automática desligada: pausa futuras cobranças, mantém o período já pago e permite retomar.
- Cancelamento imediato: encerra acesso e cancela o contrato remoto e trocas pendentes. É terminal; webhook e reconciliação não restauram benefícios.
- Arrependimento: área separada e visível em Configurações, com prazo, status e suporte. Devolve integralmente o pagamento original, sem descontar taxas da cliente. Não se confunde com cancelamento.
- Pedidos de cancelamento/reembolso são persistidos antes da rede. O worker de reconciliação tenta concluir falhas externas a cada dez minutos. Chaves de idempotência evitam estornos repetidos.
- A primeira cobrança de assinatura é assíncrona: autorização não libera acesso. A aprovação precisa existir no provedor e no ledger.

## Exclusão

Confirmação por senha. O pedido bloqueia autenticação e benefícios imediatamente, interrompe contratos atuais e agendados e só então remove dados de contato, sessões, códigos, histórico menstrual, gravidez, mensagens, memória, suporte e notificações. Imagens por usuário são removidas do R2; sua regra de expiração de um dia continua como defesa adicional. Erros de provedor/armazenamento mantêm o pedido pendente e são retomados pelo worker.

Conserva-se um registro financeiro mínimo com identificadores de pagamento, valores e datas, sem nome, CPF, celular, e-mail ou saúde da usuária. Os backups privados têm retenção de sete dias. Ao restaurar um backup anterior a uma exclusão, reaplicar os pedidos posteriores antes de reabrir o serviço. Dados mantidos pelos próprios provedores seguem suas obrigações e políticas; não prometer eliminação instantânea do histórico financeiro externo.

## Divulgação independente

Cloudflare Pages: projeto `luma-espera`, produção da branch lógica `waitlist`, domínio `espera.ia-luma.com.br`. A Vercel permanece intocada. A exportação estática reutiliza os componentes atuais de atomic design, calendário, demonstração de áudio, preços e documentos. Não inclui rotas de login, cadastro, checkout, conta ou APIs privadas.

Build: `node infra/waitlist/build.cjs`, após `npm ci` em `web`. Saída regenerável em `.backups/waitlist-build/out`. Upload de assets usa `infra/waitlist/upload-assets.cjs` e token temporário de upload do Pages. O deployment envia manifest, `_worker.js` e `_routes.json`. Segredos nunca são incluídos nos assets ou no Git.

Atualização da lista em 22/11/2026: dois checkboxes separados, desmarcados inicialmente, registram aceite dos Termos/leitura da Política e autorização específica para o aviso de lançamento. A pessoa escolhe e-mail, WhatsApp ou ambos. Só são solicitados e persistidos os contatos dos canais selecionados; WhatsApp brasileiro exige DDD válido e celular com nove dígitos. A autorização não abrange outras campanhas.

Pages Function `/api/waitlist`: valida origem, tamanho, os dois aceites, versão do formulário `2026-11-22`, versões dos documentos `2026-11-18`, contatos, honeypot e limite persistente de cinco tentativas por hora/IP. Frontend e Worker compartilham o mesmo validador. IP é armazenado apenas como HMAC temporário. D1 `luma-waitlist` conserva nome, contatos, canais, versões e data dos aceites em AES-256-GCM. A coluna legada `email_hash` permanece: recebe HMAC do e-mail, ou HMAC de `whatsapp:+55...` quando só WhatsApp é escolhido. Não há alteração estrutural do D1 nem dados de saúde. Respostas não revelam duplicidade; inscrição repetida com o mesmo contato principal preserva o registro original, sem autorizar silenciosamente novos canais. Alterações/remoções podem ser solicitadas pelo contato de privacidade.

Os cadastros antigos permanecem intactos com a autorização original de e-mail; não recebem aceite retroativo dos Termos nem autorização de WhatsApp. O exportador diferencia os campos novos dos registros legados. Selecionar WhatsApp apenas registra o interesse: esta entrega não envia mensagens nem cria um disparador de campanha. O aviso futuro precisará respeitar os canais autorizados e as exigências do provedor.

Bindings privados do Pages: `DB` aponta ao D1; `API_SECRET` protege o serviço e os buckets de limite; `ENCRYPTION_KEY` é uma chave base64 de 32 bytes. Backup das chaves está no `.env` local ignorado: `WAITLIST_API_SECRET`, `WAITLIST_ENCRYPTION_KEY`, `WAITLIST_DATABASE_ID`. Não rotacionar a chave sem migrar o acervo. Configuração de divulgação não necessita OpenAI, Twilio, Mercado Pago nem segredos de produção da Luma.

O Supabase antigo foi consultado com as credenciais fornecidas e a tabela `waitlist_signups` existe. Não foi apagada nem alterada. A lista nova não depende da instância antiga nem da AWS; contatos anteriores permanecem na origem e não se importam automaticamente textos livres que possam conter saúde.

`infra/waitlist/supabase-optional.sql` é uma migração opcional para essa tabela antiga, não executada. Ela adiciona e-mail e autorizações por canal, permite inscrição sem WhatsApp e não altera permissões/RLS. Não é necessária para o site ativo e não sincroniza Supabase com D1.

Para consultar contatos da lista nova, exporte `encrypted_contact` via D1 autenticado e use `infra/waitlist/export.cjs` localmente. A exportação contém dados pessoais: manter em local protegido e remover quando não for necessária. Exclusão de um lead pode ser solicitada pelo contato público e executada pelo seu hash, sem tornar o banco público.

Não pausar AWS automaticamente. Antes de suspender: verificar assinantes, interromper vendas e renovações que não poderão ser atendidas, validar backups, ajustar o domínio principal e comunicar a manutenção. Parar EC2 não elimina despesas de volumes, segredos e armazenamento.

## Verificação da entrega

Os testes reais de dev validaram cobrança de teste, renovação, troca de cartão, mudança anual agendada, cancelamento imediato e exclusão por senha. Não houve cobrança real nem chamadas conversacionais pagas para esses testes. O reembolso real de sandbox está pendente de autorização do Mercado Pago, conforme o documento de pagamentos; não confundir pedido persistido com dinheiro devolvido.

A lista de espera foi validada em navegador nas larguras de 390 e 1440 pixels: contraste dos textos, consentimento, botão, ausência de overflow horizontal e envio real ao D1 criptografado. O contato exclusivo desse ensaio foi removido depois da validação. Login, cadastro e checkout retornam 404 na publicação isolada do Pages. O Worker provisório redundante foi removido; a Pages Function e o D1 ativos foram preservados.

Em 22/11/2026, os testes foram escritos antes da implementação e passaram após a alteração: 17 testes web, cinco do Worker e seis fluxos de navegador (três canais em duas larguras). Build estático e lint aprovados. Deployment Pages `6c759526-a12f-446f-8e36-683eff6e63fc` publicado com sucesso. Três inscrições sintéticas reais foram gravadas, descriptografadas localmente para conferir dados/canais/aceites e removidas por IDs e hashes exclusivos, sem enviar mensagens e sem iniciar a AWS. O Supabase permaneceu intocado.

Para repetir a validação visual local, executar `node infra/waitlist/browser-test.cjs` após o build. O script usa Chrome e Playwright já configurados localmente e testa o Worker com persistência simulada. `live-smoke.cjs` é um teste explícito que cria três leads sintéticos no site publicado: só executar quando também houver acesso ao D1 para verificar e remover os hashes registrados em `.backups/waitlist-smoke.json` ao concluir.
