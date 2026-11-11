# Luma com MCP e RAG

## Estado da implementação

Fluxo integrado em C# e .NET 8. A branch `feat/mcp-and-rag` mantém o núcleo de
regras existente e adiciona uma orquestração com function calling, cliente e servidor
MCP oficiais e recuperação híbrida em PostgreSQL com pgvector.

## Caminho de uma mensagem

1. O webhook valida assinatura Twilio, conta e destinatário.
2. O backend verifica assinatura da Luma e normaliza o telefone.
3. Uma transação com advisory lock por usuária serializa o processamento. O recibo
   por provedor e MessageSid evita repetir a operação em uma reentrega.
4. O contexto inclui data no fuso da Luma, cadastro, preferências, gravidez,
   intenção pendente e os quatro turnos recentes dos últimos sete dias.
5. O cliente MCP inicializa uma sessão e descobre ferramentas com `tools/list`.
6. A OpenAI recebe os schemas dessas ferramentas. Seus `function_call` são
   encaminhados a `tools/call`; o resultado real volta como `function_call_output`.
7. O backend autoriza cada operação, executa as regras C# e devolve o estado atualizado.
8. A IA redige a resposta; guardrails e mensagens de operações assíncronas permanecem
   autoritativos. Quando o modelo falha depois da operação, retorna-se o resultado
   disponível da ferramenta.

## Transporte e autorização MCP

Usa ModelContextProtocol 2.4.0, JSON-RPC entre `StreamClientTransport` e
`StreamServerTransport`, com pipes privados dentro da API. É protocolo MCP real,
com inicialização, descoberta e invocação. Não existe endpoint MCP público.

Cada servidor é criado para uma única conversa já identificada pelo webhook.
Seu callback captura a usuária autorizada; os schemas não aceitam `user_id`,
telefone ou credenciais. Todas as ferramentas validam argumentos no servidor.
Esta entrega escolhe transporte privado para aproveitar o backend atual sem criar
um novo serviço HTTP. Um cliente MCP externo exigiria outro transporte e autenticação
própria antes de receber acesso aos dados.

As ferramentas cobrem cadastro e intenções pendentes, perfil e preferências, ciclo,
fluxo, sintomas, humor, relação sexual, calendário, gravidez, DPP, desenvolvimento,
imagens educativas, notificações e busca de conhecimento.

Há no máximo uma escrita por mensagem, cinco chamadas de ferramenta e três turnos
do modelo. Uma etapa de cadastro pode capturar vários campos explicitamente informados.
Esses limites evitam loops e controlam custos. Uma nova mensagem pode continuar a conversa.

## Conhecimento e publicação

`src/Luma.Api/Knowledge/corpus.json` contém 31 trechos gerados a partir de quatro
PDFs e 17 fichas editoriais/produto. O manifesto, páginas, hashes e licenças estão
documentados em [acervo-rag.md](acervo-rag.md). Aprovação editorial não equivale
a revisão clínica. Fontes de licença não comercial ou de conteúdo inadequado
ficam bloqueadas no inventário, não no índice.

Só documentos com status `approved` entram no índice. Conteúdo clínico mais amplo,
tratamentos ou novas orientações individuais exigem revisão profissional antes
de ampliar esse acervo. A assistente não diagnostica nem prescreve.

`KnowledgeStore.PublishAsync` calcula o hash da versão e gera embeddings somente
para uma publicação nova. O padrão usa `text-embedding-3-small` com 512 dimensões.
As instâncias compartilham um lock de publicação e não indexam o mesmo acervo duas vezes.

`luma_knowledge` guarda trechos e vetores por versão; `luma_knowledge_active`
seleciona a versão publicada. A troca é transacional. Falha durante nova publicação
preserva o índice anterior. Reativar um acervo já indexado não chama embeddings.
Uma falha de publicação interrompe o startup dessa instância, sem apagar o índice
anterior. Corrija a configuração ou restaure o acervo anterior para reiniciar;
não existe publicação parcial nem uso silencioso de conhecimento não indexado.
No acervo inicial pequeno, a busca vetorial é exata, sem índice ANN.

A busca combina similaridade de cosseno, full-text português e fusão de rankings.
Aplica visibilidade e validade de revisão antes de retornar resultados. Informações
do produto não expõem URLs internas; somente referências educativas públicas são exibidas.
Uma correspondência lexical não contorna o limiar semântico. Sem evidência,
o backend retorna o limite sem permitir uma resposta especulativa do modelo.
Quando a API de embeddings falha na consulta, há fallback full-text. Sem evidência,
a assistente informa o limite. As fontes públicas selecionadas acompanham a resposta,
com URLs preservadas pelo formatador.

Para reverter uma publicação, restaure a versão anterior do arquivo e reinicie a API.
Também é possível ajustar o ponteiro `luma_knowledge_active.release` por operação
administrativa para uma versão já existente. Uma próxima publicação no startup
volta a selecionar a versão do arquivo instalado.

## Estado e privacidade

Datas e cálculos pessoais continuam em tabelas do produto. O RAG contém somente
conhecimento geral. A IA consulta dados pessoais por ferramentas da sessão.
Não há treinamento com dados de usuárias.

`assistant_turns` guarda os recibos e a memória curta. Request e Reply usam
AES-GCM com as chaves já existentes de privacidade. Antes do consentimento,
o texto de entrada não é persistido nessa memória. Dados fora da etapa só são
guardados como intenção após consentimento e exigem confirmação ao concluir
o cadastro. A data original fica explícita, mesmo se a confirmação ocorrer depois.

A memória contextual usa sete dias e quatro turnos; registros de turnos com mais
de 30 dias são removidos quando a usuária volta a conversar. Não é um expurgo
global agendado. Os registros clínicos estruturados não são apagados por essa rotina.

Logs registram ferramentas, sucesso, versão do índice e tokens, sem conteúdo da conversa.
As chamadas novas à OpenAI usam `store=false`.

## Operação

Novas variáveis do Compose:

```env
LUMA_ASSISTANT_ENABLED=true
LUMA_ASSISTANT_MAX_MODEL_TURNS=3
LUMA_ASSISTANT_MAX_TOOL_CALLS=5
LUMA_ASSISTANT_TIMEOUT_SECONDS=10
LUMA_RAG_ENABLED=true
LUMA_RAG_EMBEDDING_MODEL=text-embedding-3-small
LUMA_RAG_DIMENSIONS=512
```

Fora do Compose, os equivalentes são `Assistant__Enabled`,
`Assistant__MaxModelTurns`, `Assistant__MaxToolCalls`,
`Assistant__TimeoutSeconds`, `Rag__Enabled`, `Rag__EmbeddingModel`
e `Rag__Dimensions`. A chave da OpenAI continua em `OpenAI__ApiKey`.

`docker compose up -d --build` aplica schema, publica o acervo e inicia as APIs.
O banco usa a imagem `pgvector/pgvector:pg16` e preserva o volume PostgreSQL 16.
Faça backup antes de atualizar uma instalação existente.

Desligar `LUMA_ASSISTANT_ENABLED` retoma o caminho legado para rollback operacional.
Os serviços novos não chamam o seletor legado de intenções.

Sandbox e produção compartilham banco, Redis, índice e assinaturas. As instâncias
MCP e os emissores de WhatsApp são separados por processo. Use conta de teste para
testes que alteram registros. Apenas a instância principal pode executar o worker
de lembretes, conforme sua configuração.

O limite de dez segundos da orquestração evita manter o webhook esperando
indefinidamente. O texto final pode voltar ao resultado autoritativo em timeout;
geração de imagem continua assíncrona no mecanismo existente.

## Validação e revisão

Testes unitários simulam respostas da OpenAI sem consumir crédito.
Testes de integração exercitam publicação, recuperação, fallback e concorrência
em PostgreSQL real. Configure `LUMA_INTEGRATION_DATABASE` com um banco isolado
para executá-los; o CI já fornece um PostgreSQL com pgvector.

Na revisão manual pelo sandbox, exercite:

- Pergunta pessoal sobre duração da menstruação e última menstruação.
- `menstruei ontem` e correção da duração habitual sem encerrar o ciclo.
- `menstruei hoje` durante a pergunta do nome, conclusão e confirmação posterior.
- Cadastro com `não lembro` para a data, alternativas numéricas e linguagem informal.
- Pergunta geral sobre previsão usando fontes.
- Ausência de evidência e tentativa de sair do escopo.
- Gravidez relatada, semanas, DPP, tamanho educativo e sangramento.
- Horários distintos de notificação, Essencial e bloqueio no Básico.
- Reentrega do mesmo MessageSid sem duplicação.

Chamadas reais usadas na validação são poucas e registradas no PR. O teste HTTP
assinado percorre a URL pública do sandbox e o protocolo MCP; ele não substitui
a verificação visual de entrega no aplicativo WhatsApp do celular.
