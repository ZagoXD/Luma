# Revisão de prontidão para produção

Data: 19/11/2026. Revisão do commit `1a2ea98df5ae94a8453ed6b666fe5a3ec00e48e5`, publicado em dev e produção. Esta revisão não alterou código de aplicação nem executou cobranças, SMS, e-mails ou chamadas pagas de IA.

## Parecer

**Resolução posterior:** os achados abaixo registram o estado anterior à correção. A implementação está documentada em [Sessões e segurança de produção](seguranca-producao-dotnet10.md): .NET 10, dependências corrigidas, sessão curta/refresh revogável, limites persistentes, notificações e privacidade fail-closed. Termos e privacidade foram revisados e aprovados pelo responsável. Não interpretar esta lista histórica como uma nova pendência de implementação; Sender e confirmação do estorno continuam externos.

Os fluxos principais estão implementados e os testes automatizados passaram. Entretanto, não se recomenda declarar a abertura pública pronta apenas com as pendências do Sender e do reembolso: a revisão encontrou problemas adicionais de segurança e uma inconsistência de notificações.

## Achados

### P1 — Proteção insuficiente contra abuso de autenticação e SMS

O reenvio de SMS tem contador de 30 segundos apenas no frontend (`web/components/templates/LoginPageTemplate.tsx:126`). `AccountPhoneVerificationService.CreateAndSendCodeAsync` consome os códigos anteriores e solicita outro envio sem cooldown, cota própria ou reserva atômica por conta/número. O login em `Program.cs:307` também não aplica limitação própria. Não há limitador correspondente no proxy Nginx versionado. Recarregar o cliente ou usar a API contorna o temporizador visual; não foram enviados SMS para reproduzir esse abuso. Limites internos da Twilio não substituem controles e orçamento da aplicação.

Antes da abertura, impor limites no servidor para login, cadastro, recuperação, verificação e troca de celular. Para SMS, manter cooldown e cotas por conta e destino, com controle de concorrência e resposta segura. Aplicar limites também na API direta, não somente no BFF web; preservar os webhooks assinados em política separada.

### P1 — Redefinir senha não revoga JWTs anteriores

`EmailServices.cs:488` troca o hash da senha e consome o token de recuperação. A autenticação em `Program.cs:1115` verifica assinatura, expiração e existência da conta, sem versão de sessão ou data de revogação. Os JWTs duram 30 dias (`Program.cs:1145`). Um JWT já obtido antes da troca continua aceito depois dela. A exclusão de conta bloqueia a autenticação; o problema específico é a recuperação de senha e a ausência de revogação individual/global de sessões.

Antes da abertura, introduzir versão de sessão/security stamp ou sessões revogáveis, invalidar tokens antigos ao redefinir senha e adicionar regressão que tente autenticar com o token anterior.

### P1 — Plataforma sem suporte e dependência vulnerável no backend

O projeto e os containers usam .NET 8 (`Luma.Api.csproj:4`, `Dockerfile:1`). O suporte terminou em 10/11/2026, antes desta revisão. Planejar migração para uma versão LTS suportada, incluindo testes, EF Core/Npgsql compatíveis, imagens e CI.

A auditoria NuGet detectou `Microsoft.Bcl.Memory 9.0.0` na API, transitivo de `Twilio 8.2.0`/IdentityModel. O aviso CVE-2026-26127 é de severidade alta, relacionado a negação de serviço na decodificação Base64Url. Não foi demonstrada exploração por uma rota da Luma; a presença da dependência vulnerável é confirmada. Corrigir a árvore e repetir auditoria/build/testes. `System.Net.Http 4.3.0` e `System.Text.RegularExpressions 4.3.0` também aparecem como vulneráveis no projeto de testes, não como dependências reportadas da API.

Fontes: [suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy), [comunicado sobre .NET 8/9](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/), [aviso Microsoft.Bcl.Memory](https://github.com/advisories/GHSA-73j8-2gch-69rq).

### P2 — Edição das notificações após desligar renovação

`GetVisibleSubscriptionAsync` inclui assinaturas `canceled` ainda pagas, e o worker continua enviando notificações enquanto houver vigência. Porém, o POST de preferências (`Program.cs:521`) exige `active` e rejeita essas mesmas usuárias. Além disso, usa `Results.Forbid()` sem serviço/esquema de autenticação registrado para executá-lo. Uma chamada controlada em dev, com a conta exclusiva de QA já sem benefício, confirmou HTTP 500 em vez de 403. Não houve envio de notificação nesse ensaio.

Unificar a decisão de benefício vigente, preservar edição para Essencial pago com renovação desligada e devolver 403 explícito para conta sem direito. Cobrir Básico, expirado, encerrado, Essencial ativo e Essencial vigente sem renovação em testes de endpoint.

### P2 — Configuração de privacidade falha aberta

`PrivacyRuntime.Configure` aceita chave inválida e fallback de pepper; `EncryptionEnabled` passa a falso quando a chave não é válida (`PrivacyServices.cs:27-42`). Isso pode permitir gravação sem a proteção esperada após uma configuração incorreta. As chaves atuais dos dois ambientes foram conferidas: criptografia habilitada, chave e pepper com 32 bytes, distintos entre ambientes. Não foi observada gravação atual sem criptografia.

Endurecer a inicialização de produção para interromper o startup diante de chave/pepper ausente ou inválido e proibir fallback de desenvolvimento. Não rotacionar as chaves existentes como parte dessa correção.

## Validações concluídas nesta revisão

| Área | Resultado e limite |
| --- | --- |
| Backend | 289 testes aprovados, nenhum ignorado; banco PostgreSQL exclusivo dos testes, removido ao final. |
| Frontend | 10 testes, lint, TypeScript e build de produção aprovados. |
| Infraestrutura | 15 testes aprovados. |
| Lista de espera | 2 testes de proteção de dados aprovados; site publicado responde HTTP 200. |
| Dependências do frontend | `npm audit --omit=dev`: nenhuma vulnerabilidade conhecida reportada. |
| Produção/dev | Serviços publicados disponíveis; API de produção com `/health` HTTP 200. |
| APIs privadas | `/account/me` sem autenticação devolve 401; Swagger não é publicado em produção. |
| Webhooks | Mercado Pago sem assinatura devolve 401; Twilio sem assinatura devolve 403. |
| Documentos públicos | Termos e privacidade respondem 200 nos três sites: produção, dev e espera. |
| Configuração | Cookies seguros, exigência de plano e criptografia habilitados; JWT, chave e pepper distintos entre dev/prod. |
| Isolamento de provedores | Produção configurada com pagamentos reais, verificação real e worker ativo; dev com pagamentos de teste, verificação simulada e worker desativado. Não foram disparadas operações pagas. |
| Operação AWS | Containers em execução, PostgreSQL/Redis saudáveis, timers de deploy/backup ativos; dump de produção no S3 datado de 19/11/2026. Disco com aproximadamente 23 GB livres. |
| Testes manuais informados | Responsável confirmou assinaturas, conversas, e-mails, aceites e SMS. São evidência adicional, não reexecução independente dessas chamadas nesta revisão. |

Total automatizado: 316 testes aprovados. O primeiro ensaio local usou incorretamente o bind `127.0.0.1:5433` como porta; o harness foi corrigido e a suíte completa passou no banco isolado. Isso não foi tratado como defeito da aplicação.

## Pendências externas e limites

- Sender alfanumérico da Twilio em análise. Distinguir a apresentação do remetente da entrega funcional de SMS, já testada pelo responsável.
- Reembolso real de sandbox do Mercado Pago ainda recusado com HTTP 401; pedido persistido, mas sem confirmação de devolução. Validar autorização do token, estorno integral, retentativa idempotente e efeito na assinatura. Não declarar dinheiro devolvido com base apenas em teste simulado.
- Confirmar com o provedor como ficam as tarifas do vendedor. A política de reembolso à cliente não deve depender da devolução dessas tarifas.
- Revisão jurídica dos documentos e validação editorial de saúde continuam sendo responsabilidades humanas; testes técnicos não constituem certificação jurídica ou médica.
- Não houve pentest, teste de carga ou nova verificação de entrega de templates/áudio/imagens nesta revisão. Esses limites impedem garantia absoluta de ausência de falhas.

## Ordem recomendada

1. Corrigir limitação de autenticação/SMS e revogação de sessões.
2. Migrar a plataforma suportada, sanar dependências e fazer a privacidade falhar fechada.
3. Corrigir o endpoint de preferências e acrescentar testes de autorização HTTP.
4. Repetir a suíte, auditorias, deploy em dev e smoke tests; promover a mesma revisão para produção.
5. Fechar as pendências de Sender/reembolso antes de anunciar esses fluxos como plenamente validados.
