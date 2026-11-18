# SEO e Indexação da Luma

## Diagnóstico

Na auditoria de 18/11/2026, o site tinha título e descrição genéricos, sem ícone de aba próprio, sitemap ou canonical. O `/robots.txt` público era fornecido pela Cloudflare e permitia busca tradicional; isso não demonstrava um bloqueio do Google. A ausência em uma busca por marca, isoladamente, não confirma se uma URL está ou não indexada. O diagnóstico definitivo depende do Search Console.

## Implementação

- Identidade `Luma IA` no título, descrição e dados estruturados públicos de WebSite/Organization.
- Ícone PNG de 96×96, Apple Touch Icon de 180×180 e imagem de compartilhamento de 1200×630, gerados pelas convenções de metadata do Next.js.
- Canonical da página inicial para `https://ia-luma.com.br/` e redirecionamento permanente de `www` para o domínio principal, preservando caminho e query.
- Sitemap somente com a página pública existente, sem URLs de contas, pagamentos ou calendário pessoal.
- Metadata e cabeçalho `X-Robots-Tag` com `noindex` em dev e nas áreas de conta, API, checkout, login e recuperação de acesso.
- Dev não publica URLs no sitemap. Seu robots permite leitura para que buscadores possam reconhecer o `noindex`; bloquear o rastreamento não é uma forma confiável de remover URLs já conhecidas do índice.
- Metadados em PT-BR e Open Graph/Twitter para compartilhamento.

Não foram publicadas informações do acervo RAG, dados pessoais, avaliações inventadas ou credenciais médicas inexistentes. `noindex` não substitui autenticação e autorização, que continuam obrigatórias para acessar dados privados.

## Configuração por Ambiente

```dotenv
# Web de produção
LUMA_SITE_URL=https://ia-luma.com.br

# Web de dev
LUMA_SITE_URL=https://dev.ia-luma.com.br

# Docker local
LUMA_SITE_URL=http://localhost:3000
```

Somente a origem HTTPS exata de produção permite indexação. A configuração é lida no servidor a cada requisição; não depende de `NEXT_PUBLIC_*` congelado durante o build. Isso permite promover a mesma imagem Docker de dev para produção sem carregar a política de indexação do ambiente errado. O layout é renderizado no servidor, inclusive para fornecer metadata antes da execução de JavaScript no navegador.

Na AWS, a variável fica em `web.LUMA_SITE_URL` dos segredos de cada ambiente. O gerador `prepare-config.cjs` também prepara esse campo. Alterar só o `.env` local não muda o ambiente AWS.

## Google Search Console: Ação do Responsável

1. Acessar o [Google Search Console](https://search.google.com/search-console/).
2. Adicionar uma propriedade do tipo **Domínio** com `ia-luma.com.br`, sem protocolo ou caminho.
3. Verificar a propriedade pelo TXT DNS informado pelo Google. Se o fluxo oferecer integração automática com a Cloudflare, ela também pode ser usada. Não inventar o token de verificação.
4. Em **Sitemaps**, enviar `https://ia-luma.com.br/sitemap.xml`.
5. Inspecionar `https://ia-luma.com.br/`, executar o teste da URL publicada e solicitar indexação uma vez.
6. Acompanhar relatórios de indexação e desempenho, principalmente problemas de rastreamento, canonical e páginas excluídas.

O proprietário pode fornecer o valor TXT de verificação para configuração no DNS; não é necessário compartilhar sua senha Google. O registro não é uma credencial para acessar as contas das usuárias.

Sitemap e solicitação de rastreamento não garantem indexação nem posição nas buscas. O Google informa que o processo pode levar dias ou semanas. Consulte a [orientação oficial de solicitação de rastreamento](https://developers.google.com/search/docs/crawling-indexing/ask-google-to-recrawl).

## Validação

```shell
cd web
npm test
npm run lint
npm run build
node scripts/check-seo.mjs --local
node scripts/check-seo.mjs https://ia-luma.com.br production
node scripts/check-seo.mjs https://dev.ia-luma.com.br development
```

O CI verifica as políticas puras e inicia a mesma versão standalone em modos produção/dev para checar HTML, canonical, robots, sitemap, bloqueio de indexação das áreas privadas e dimensões reais dos PNGs. O teste não chama OpenAI, Twilio, ElevenLabs ou Stripe.

Após deploy, conferir também o redirecionamento de `https://www.ia-luma.com.br/`. A Cloudflare pode acrescentar suas próprias regras ao robots; preserve as políticas de bloqueio de treinamento sem confundi-las com bloqueio de busca tradicional.
