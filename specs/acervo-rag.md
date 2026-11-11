# Acervo e importação do RAG

Atualização: 10/11/2026. Revisão **editorial e técnica**, não validação clínica independente.

## O que foi concluído

- Importação em C# com PdfPig, hashes SHA-256, páginas selecionadas e geração reproduzível.
- Quatro PDFs indexados, 17 fichas editoriais/produto e 31 trechos no total.
- Classificação de acesso antes da vetorização e na recuperação.
- Proveniência por documento, página, versão, hash, revisão e licença.
- Avaliações de recuperação, ausência de evidência e privacidade em PostgreSQL real.
- Preços confrontados por teste com o catálogo do frontend.

Não significa que todo PDF disponível na internet foi importado, nem que a Luma
passou a realizar aconselhamento clínico. O escopo publicado é o inventário abaixo.

## Inventário

| Fonte | Seleção | Situação |
| --- | --- | --- |
| Office on Women's Health/HHS, Menstrual Cycle | 2 páginas | Indexado; o PDF declara texto livre de restrições de copyright |
| Office on Women's Health/HHS, Pregnancy Tests | 2 páginas | Indexado para educação geral, sem interpretação individual |
| Office on Women's Health/HHS, Breastfeeding | 2 páginas | Indexado para informação geral e encaminhamento |
| UFRR, Métodos Contraceptivos Disponíveis no SUS, 2025 | Páginas físicas 5 e 6 | Indexado; licença CC BY-SA 3.0 BR indicada no repositório |
| Ministério da Saúde, Caderneta Brasileira da Gestante, 2026 | Nenhuma | Bloqueado: CC BY-NC-SA; autorização comercial não esclarecida |
| Programa Dignidade Menstrual, 2024 | Nenhuma | Bloqueado: permissão comercial e atualização das regras não verificadas |
| OWH, Birth Control Methods | Nenhuma | Bloqueado: referências a opções antigas e contexto regulatório estrangeiro |

As páginas da UFRR sobre percentuais de eficácia, esquemas de uso e detalhes de
métodos foram excluídas. A Luma não prescreve e esses dados exigem revisão clínica
atual antes de qualquer ampliação. Capas e referências também não entram na busca.

Os PDFs OWH têm datas originais anteriores a 2026, preservadas no manifesto.
Disponibilidade atual no portal não equivale a revisão clínica recente. Não usar
essas fichas como protocolos de tratamento, promessas de precisão ou serviços do SUS.
As fontes em inglês são consultadas semanticamente; a resposta deve permanecer em PT-BR.

Links de origem e licença:

- [Fichas e declaração de reutilização OWH/HHS](https://womenshealth.gov/patient-materials/resource/fact-sheets).
- [Cartilha UFRR e licença do repositório](https://educapes.capes.gov.br/handle/capes/1001159).
- [CC BY-SA 3.0 Brasil](https://creativecommons.org/licenses/by-sa/3.0/br/).
- [Caderneta Brasileira da Gestante](https://bvsms.saude.gov.br/bvs/publicacoes/caderneta_brasileira_gestante.pdf), licença na página física 107.

Os trechos textuais da cartilha UFRR no corpus são disponibilizados sob CC BY-SA
3.0 BR, conforme o repositório de origem. A extração remove arte gráfica e pode
normalizar espaços e hifenização. Essa licença se aplica aos trechos dessa obra,
não atribui licença ao restante do software nem implica endosso dos autores.
Crédito: Abraão Souza Porto, Arthur Eduardo Cordeiro da Silva, Naine da Silva
Alcantara, Rosangela dos Santos Bandeira e Gleidilene Freitas da Silva, UFRR, 2025.

## Conteúdo próprio da Luma

As fichas cobrem identidade, limites, maioridade e consentimento, assinatura mensal
e anual, pagamento e cancelamento, cadastro fora da ordem, registros, calendário,
gravidez, áudio, imagens, notificações, privacidade e suporte.

Os valores refletem o catálogo atual: Básico R$ 11,00/mês ou R$ 70,80/ano;
Essencial R$ 20,00/mês ou R$ 118,80/ano. Os equivalentes anuais R$ 5,90 e
R$ 9,90 não são parcelamento. Um teste acusa divergência com os preços da web.

Plano da pessoa, validade, datas menstruais e gravidez continuam sendo dados
consultados por ferramentas autorizadas. O acervo não substitui essas leituras.

## Privacidade e exibição

- `public`: conteúdo educativo com fonte pública e atribuição quando necessária.
- `assistant`: informações do produto autorizadas para explicar às usuárias,
  sem expor arquivo, endereço interno ou referência de implementação.
- `restricted`: nunca publicado no índice da assistente e filtrado novamente na busca.

`assistant` **não é cofre de segredos**: seu conteúdo pode ser explicado à usuária.
Credenciais, estratégia confidencial, dados pessoais e documentos secretos não
devem entrar no acervo. Restrição por prompt não substitui o filtro de acesso.
Os textos recuperados são enviados à OpenAI; armazenamento privado não significa
inferência exclusivamente local.

Os PDFs originais ficam em `whatsapp-app/.knowledge-sources`, ignorada pelo Git e
excluída do contexto Docker. Não há endpoint de download nem upload em bucket
público. As referências públicas permanecem distintas de documentos internos.
Não usamos imagens, logos ou formulários de usuárias como conhecimento.

## Reprodução da importação

Arquivos em `whatsapp-app/src/Luma.Api/Knowledge`:

- `sources.json`: manifesto de PDFs, aprovação, licença, versão, hash e seleção.
- `editorial.json`: conteúdo editorial próprio e sínteses educativas com fontes.
- `corpus.json`: saída gerada usada para publicar os embeddings.
- `ingestion-report.json`: páginas incluídas/excluídas e fontes bloqueadas.
- `evaluation-cases.json`: perguntas de regressão de recuperação.

No PowerShell, a partir de `whatsapp-app`:

```powershell
$root = (Get-Location).Path
dotnet run --project src/Luma.Api --no-launch-profile -- --ingest-knowledge "$root/src/Luma.Api/Knowledge/sources.json" "$root/.knowledge-sources" "$root/src/Luma.Api/Knowledge/corpus.json"
docker compose up -d --build api api-sandbox
```

Sem os originais, o importador baixa somente os endereços HTTPS permitidos no
manifesto. Downloads têm limite de tamanho e não seguem redirecionamentos.
Mudança de hash interrompe a importação: revisar o novo arquivo antes de atualizar
o manifesto. Textos vazios ou com caractere de substituição também interrompem;
PDF escaneado exige OCR e revisão antes de ser aprovado. Não há OCR silencioso.

Inspeção local antes da seleção, sem publicar:

```powershell
dotnet run --project src/Luma.Api --no-launch-profile -- --inspect-knowledge "$root/.knowledge-sources/owh-ciclo.pdf"
```

A importação não chama IA. Embeddings são gerados na publicação de uma versão nova,
usando a configuração existente da OpenAI. O startup não baixa nem lê PDFs; usa
o corpus preparado. Não há novas credenciais necessárias.

## Sem evidência, sem resposta inventada

Busca vetorial abaixo do limiar não é aceita por compartilhar uma palavra.
O fallback textual exige os termos da consulta, em vez de um OR amplo.
Ausência de resultado retorna imediatamente uma mensagem autoritativa de limite;
o modelo não recebe uma nova oportunidade para substituí-la por um palpite.

Trechos recuperados são contexto, não uma resposta pronta. Falha ou timeout na
geração não devolve o texto bruto desses documentos. Se o trecho não responder
à pergunta, a instrução é declarar o limite e, para questões de saúde, encaminhar
a profissional. Isso reduz riscos, mas não constitui garantia matemática contra
todo erro de um modelo generativo. Avaliações e revisão profissional continuam necessárias.

## Pendências humanas e atualização

Não há pendência de integração para os quatro PDFs aprovados e fichas publicadas.
Para ampliar a cobertura clínica, um profissional deve validar o conteúdo, sua
atualidade e a adequação ao Brasil. Para liberar fontes NC ou de licença incerta,
obter autorização ou avaliação jurídica apropriada; não basta renomear seu status.
As fontes não autorizadas permanecem bloqueadas por padrão.

Revisões vencidas são excluídas da publicação e da busca. Verificar os prazos do
manifesto e das fichas antes do vencimento; alterações de preço exigem atualização
do acervo e dos testes. Aprovação editorial não deve ser promovida a clínica
sem que essa revisão tenha de fato ocorrido.
