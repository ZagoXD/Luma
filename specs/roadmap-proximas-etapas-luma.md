# Roadmap da Luma

Última atualização: 03/05/2026.

## Estado Atual

A Luma está na reta final da V1 operacional. O MVP de WhatsApp, cadastro, ciclo menstrual, gravidez, web, Stripe, Redis, notificações estruturais, áudio, imagem educativa, calendário e criptografia de dados reais foi implementado.

Status resumido:

- Etapa 1: concluída.
- Etapa 1.1: concluída.
- Etapa 2: concluída.
- Etapa 3: concluída.
- Etapa 4: concluída como arquitetura inicial com OpenAI, tools e RAG interno simples.
- Etapa 5: implementada como primeira versão SaaS.
- Etapa 5.1: concluída.
- Etapa 6: parcialmente concluída.
- Etapa 7: concluída como primeira camada de privacidade em repouso.

## Etapa 1 - App Inicial e Cadastro

Status: concluída.

Inclui:

- API C#.
- Docker.
- PostgreSQL.
- Twilio WhatsApp webhook.
- Cadastro inicial.
- Consentimento.
- Nome.
- Maioridade.
- Dados básicos de ciclo.
- Persistência no banco.

## Etapa 1.1 - Método Contraceptivo

Status: concluída.

Inclui:

- Pergunta sobre método contraceptivo no fim do cadastro.
- Normalização de respostas como pílula, injeção, DIU, implante, camisinha, nenhum, outro e prefiro não informar.
- Persistência no banco.

## Etapa 2 - Ciclo Menstrual Completo

Status: concluída.

Inclui:

- Início e término da menstruação.
- Fluxo.
- Sintomas.
- Humor.
- Duração média.
- Estimativa de próxima menstruação.
- Estimativa de atraso.
- Intenções fora de ordem.
- Consulta de dados históricos.

## Etapa 3 - Gravidez

Status: concluída.

Inclui:

- Modo gravidez.
- Data da última menstruação.
- Semanas de gestação.
- Data provável do parto.
- Sangramento na gravidez.
- Sintomas.
- Consulta pré-natal.
- Ultrassom.
- Guardrails médicos.

## Etapa 4 - Luma Inteligente com IA, RAG e Tools

Status: concluída como V1 inicial.

Inclui:

- OpenAI como motor de IA.
- Ollama removido.
- Tools internas.
- Backend autoritativo.
- Resposta final humanizada pela IA.
- RAG interno simples.
- Guardrails fixos.

Próxima evolução:

- RAG real com embeddings.
- Avaliação automática de qualidade.
- Observabilidade de prompts, custo e latência.

## Etapa 5 - SaaS Web e Assinaturas

Status: implementada.

Inclui:

- Landing page.
- Login.
- Cadastro web.
- Perfil.
- Planos.
- Stripe Elements.
- Assinaturas.
- Cancelamento.
- Retomada.
- Troca de plano.
- Troca de cartão.
- Webhooks Stripe.

## Etapa 5.1 - WhatsApp Responde Apenas Assinantes

Status: concluída.

Inclui:

- Validação de plano ativo pelo número de telefone.
- Normalização de celular brasileiro.
- Bloqueio de conversa quando não há plano válido.

## Etapa 6 - Operação e Proteções

Status: parcialmente concluída.

Implementado:

- Rate limit com Redis.
- Deduplicação de webhook.
- Lock por telefone.
- Bloqueio defensivo de grupo.
- Worker de notificações estruturado.
- Preferências de notificação.
- Transcrição de áudio via ElevenLabs para plano Essencial.
- Bloqueio de áudio, imagens e notificações para plano Básico.

Pendente:

- Criar/aprovar templates Twilio/Meta.
- Ativar worker de notificações em produção.
- Testar mensagens proativas reais fora da janela de 24h.
- Melhorar painel administrativo.
- Adicionar monitoramento de erros/custos.

## Etapa 7 - Privacidade e Criptografia de Dados Reais

Status: concluída como primeira camada operacional.

Implementado:

- Criptografia de campos sensíveis com AES-GCM.
- Hashes HMAC para busca por e-mail, CPF e telefone.
- Backfill automático no startup da API para dados já existentes.
- Busca por `EmailHash`, `CpfHash` e `PhoneHash` nos fluxos principais.
- Criptografia de e-mail, CPF, nome, telefone, nome de exibição, telefone de assinatura, metadados de eventos, payloads pendentes, corpo de mensagens quando salvo, método contraceptivo e conversas bloqueadas.
- Variáveis `Privacy__EncryptionEnabled`, `Privacy__EncryptionKey`, `Privacy__LookupPepper` e `Privacy__ActiveKeyId`.

Observação:

- Datas operacionais continuam em colunas próprias para manter calendário, notificações e cálculos funcionando.
- Uma próxima evolução pode criptografar datas exatas usando índices cegos por mês/dia.

## Meta para V1.0.0

Para considerar a V1.0.0 pronta para piloto:

1. API e Web deployadas no Render ou ambiente equivalente.
2. Postgres gerenciado configurado.
3. Redis Cloud configurado.
4. Stripe em modo produção com produtos e preços reais.
5. Twilio WhatsApp Business fora do Sandbox.
6. Templates de notificação aprovados.
7. Worker de notificações habilitado.
8. Logs e alertas mínimos configurados.
9. Política de privacidade e termos publicados.
10. Testes principais passando no CI.
11. Chaves de privacidade configuradas e guardadas com segurança no ambiente de produção.

## Pós-V1

Possíveis etapas para V2:

- Painel administrativo.
- Métricas de retenção e uso.
- RAG com base médica revisada.
- Histórico visual do ciclo.
- Exportação de dados da usuária.
- Lembretes avançados por tipo de contraceptivo.
- Suporte a reembolso/gestão financeira mais completa.
- Melhorias de LGPD, auditoria e exclusão de conta.
- Criptografia de datas exatas com índices cegos.
