# Sessões e segurança de produção

Atualização: 19/11/2026. Escopo: correção dos itens 2 a 6 da revisão de prontidão. Termos e privacidade da versão `2026-11-18` foram revisados e aprovados pelo responsável; seus aceites permanecem válidos.

## Plataforma

API e testes usam `net10.0`, EF Core 10.0.15, Npgsql EF 10.0.3 e imagens oficiais SDK/ASP.NET 10. O `global.json` seleciona SDK estável 10.0.400 ou feature band posterior da mesma versão principal. Não se migra para .NET 11. Swashbuckle foi atualizado e as chamadas obsoletas a `WithOpenApi` removidas.

`Microsoft.Bcl.Memory` está explicitamente atualizado para 10.0.15. Auditoria NuGet inclui transitivos e bloqueia avisos de severidade alta/crítica no restore, inclusive no CI. As dependências de testes também foram atualizadas. O CI audita as dependências de produção npm.

## Sessão

- Access token JWT assinado com HS256, vida de dez minutos, `typ=at+jwt`, emissor e audiência validados, `sub`, `sid`, versão da conta e `jti`. Nenhum e-mail, CPF ou celular é incluído no token.
- Refresh com prazo absoluto de trinta dias, sem extensão silenciosa a cada renovação. A API mantém somente seu hash corrente, contador da geração e metadados da sessão no PostgreSQL. O cliente trata o refresh como opaco.
- A prova do refresh usa HMAC-SHA256 com separação de finalidade; cada geração pertence a uma sessão e versão da conta. A troca ocorre sob lock transacional no PostgreSQL. Não há segredo de refresh reversível salvo no banco.
- Uma janela fixa de quinze segundos permite repetir a geração imediatamente anterior em chamadas concorrentes, devolvendo o mesmo sucessor. A janela não é prorrogada pelo reenvio. Reutilizar uma credencial antiga legítima fora dela revoga aquela família. Uma prova forjada não revoga uma sessão de terceiros.
- Cada acesso autenticado também verifica a sessão no banco, sua revogação, validade absoluta, versão da conta e ausência de exclusão. Portanto, logout e recuperação de senha não precisam esperar dez minutos para bloquear acesso.
- Recuperação de senha consome o link sob lock por conta, invalida os demais links da versão anterior, incrementa a versão da conta e revoga suas sessões. Duas chamadas concorrentes não conseguem usar o mesmo link. As senhas novas usam PBKDF2-SHA256 com 600 mil iterações e salt aleatório; hashes antigos de 100 mil continuam verificáveis. Não há migração destrutiva de senhas.
- Cookies host-only `__Host-luma_access` e `__Host-luma_refresh`, `HttpOnly`, `Secure` em HTTPS, `SameSite=Lax`, sem `Domain`. No localhost HTTP usam `luma_access` e `luma_refresh`. O cookie legado `luma_session` não autentica mais.
- Lax preserva navegação de retorno de provedores de pagamento. POSTs no BFF exigem origem exata e rejeitam `Sec-Fetch-Site: cross-site`; SameSite não é a única proteção contra CSRF.
- O BFF não retorna os tokens ao JavaScript nem os guarda em localStorage. Diante de 401, tenta renovar uma vez, grava os novos cookies e repete a chamada autenticada. Credencial de renovação inválida limpa os cookies e exige novo login. Indisponibilidade/429 não é confundida com revogação.
- Login e cadastro mantêm somente dados de perfil/estado no JSON destinado ao navegador. O destino pós-login aceita apenas rotas internas, bloqueando URLs externas, `javascript:` e escapes de barra/controle.

Os endpoints da API destinados ao BFF são `/account/refresh` e `/account/logout`. Ambos usam o refresh no corpo da chamada entre servidores, nunca em URLs. O browser utiliza `/api/auth/refresh` e `/api/auth/logout`, sem manipular o valor do cookie HttpOnly.

## Proteção contra abuso

Contadores em `auth_rate_limits`, com chave HMAC e expiração, sobrevivem a reinícios e são compartilhados entre réplicas. O incremento PostgreSQL é atômico. Não se armazenam IPs/e-mails/celulares em claro nessa tabela.

| Operação | Limite |
| --- | --- |
| Login por e-mail normalizado | Cinco tentativas por cinco minutos |
| Cadastro por e-mail | Cinco tentativas por hora |
| Recuperação por e-mail | Três envios por quinze minutos, sempre com resposta genérica |
| Reenvio de código por destino, inclusive troca de celular | Um a cada trinta segundos |
| Código por destino | Cinco envios por hora |
| Código por conta, somando destinos | Oito envios por hora |
| Rotas de autenticação por conexão de rede e rota | Trezentas solicitações por cinco minutos |

O limite de rede é uma defesa adicional: atrás do BFF pode representar uma conexão compartilhada por várias usuárias. Os limites específicos de identidade/destino não dependem de `X-Forwarded-For` informado pelo cliente e não podem ser contornados trocando IP. As cotas são conservadoras para a escala inicial e devem ser acompanhadas durante a homologação. Limites da Twilio permanecem como defesa adicional, não substituta.

Bloqueio devolve 429 com `Retry-After`; não chama o provedor nem invalida o código anterior. Uma tentativa de envio que falhou no provedor ainda consome a reserva, evitando tempestade de retentativas. O temporizador do frontend é apenas conveniência. As rotas de autenticação têm corpo limitado a 8 KiB. Contadores vencidos há mais de um dia são limpos nas operações seguintes.

## Benefícios e notificações

A renovação desligada usa status `canceled`, mas não remove o período pago. `BillingPolicy.HasAccess` exige período vigente, status ativo ou sem renovação e ausência de término definitivo. Essencial válido pode editar preferências; Básico, expirado, pendente ou encerrado recebe 403 JSON, nunca `Results.Forbid()` sem esquema registrado. Horários separados e os demais critérios do worker permanecem inalterados.

## Privacidade e migração

Em produção, a criptografia não pode ser desativada. Chave e pepper devem ser base64 válidos de exatamente 32 bytes, com identificador de chave. Com criptografia habilitada, a validação também é obrigatória em dev. Configuração inválida interrompe a inicialização antes de acessar o banco; não há fallback silencioso para gravar dados pessoais sem proteção. Não foram rotacionadas as chaves existentes.

A atualização aditiva cria os contadores e acrescenta metadados às contas/sessões antes do backfill de privacidade. Não altera IDs, dados menstruais, planos nem o acervo RAG. JWTs antigos de trinta dias deixam de funcionar; usuárias devem entrar novamente. O rollback de imagens continua possível, mas não deve ser usado para reabrir a autenticação antiga sem avaliar os riscos corrigidos.

Nenhum segredo novo é necessário. Mantêm-se `LUMA_JWT_SIGNING_KEY`/`Luma__JwtSigningKey`, as chaves de privacidade e `LUMA_COOKIE_SECURE=true` em HTTPS. O exemplo de `.env` não inclui chave JWT utilizável; gere uma com `openssl rand -base64 48`. Cookie seguro permanece falso somente para localhost HTTP. Produção e dev mantêm chaves diferentes.

## Verificação e pendências

Testes cobrem expiração absoluta, rotação, replay, logout, recuperação, concorrência PostgreSQL, limites sem chamadas de provedor, preferências por status/plano e atualização do schema anterior. O harness de HTTP bloqueia qualquer rede externa. O navegador é exercitado contra API e banco isolados, verificação simulada e provedores desativados, incluindo renovação automática em chamadas paralelas.

Não foram autorizadas chamadas de teste pagas a Twilio, OpenAI ou ElevenLabs. A aprovação do Sender e a confirmação do estorno Mercado Pago continuam sendo as duas pendências externas. A entrega técnica habilita homologação controlada; não representa garantia absoluta de segurança ou de respostas médicas. Monitoramento, backups e resposta a incidentes continuam rotinas permanentes.

Referências: [JWT no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0), [replay e rotação de refresh](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14), [suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy).
