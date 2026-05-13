# Task — Solicitação de suporte por e-mail com Resend

## Contexto

Implementar um fluxo simples de suporte para a Luma sem criar, neste momento, um painel administrativo completo de tickets.

A usuária terá uma tela de **Precisa de ajuda / Suporte** dentro do frontend. Ao enviar uma solicitação, o sistema deve:

1. registrar uma solicitação básica no banco;
2. enviar um e-mail automático para a usuária confirmando o recebimento;
3. enviar um e-mail para a caixa de suporte com os dados da solicitação e anexos;
4. permitir que a resposta humana aconteça fora do sistema, manualmente pelo Gmail `lumasuporte.ia@gmail.com`.

Este fluxo deve ser tratado como **solicitação simples de suporte por e-mail**, não como sistema completo de tickets.

---

## Objetivo

Criar uma tela simples de suporte no app da Luma onde a usuária consiga enviar uma solicitação com assunto, descrição e anexos opcionais.

A Luma deve usar o Resend para enviar:

- um e-mail de confirmação para a usuária;
- um e-mail para a equipe de suporte em `lumasuporte.ia@gmail.com`, contendo os dados da solicitação e os anexos enviados.

Os anexos devem ir diretamente no e-mail enviado para suporte. Eles **não devem ser salvos no banco de dados**.

---

## Fora de escopo

Não implementar neste momento:

- painel administrativo de suporte;
- login/permissão de admin;
- tela de listagem de tickets;
- tela interna de resposta;
- histórico de mensagens dentro do app;
- status avançados de atendimento;
- notificações de “ticket respondido”;
- chat interno;
- uso de `suporte@ia-luma.com.br` como caixa corporativa;
- armazenamento de anexos no banco;
- upload dos anexos para Cloudflare R2 ou outro storage.

---

## Fluxo funcional

### 1. Acesso ao suporte

No frontend, adicionar uma entrada visível para suporte, preferencialmente na tela de perfil da usuária.

Exemplos de posicionamento:

- card na tela de perfil: **Precisa de ajuda?**;
- botão: **Abrir solicitação de suporte**;
- link secundário: **Fale com o suporte**.

A ação deve abrir uma nova tela ou modal de suporte.

Sugestão de rota:

```txt
/profile/support
```

Ou, se fizer mais sentido para a estrutura atual:

```txt
/support
```

---

### 2. Formulário de suporte

A tela deve conter:

- `subject` / assunto — obrigatório;
- `description` / descrição — obrigatório;
- `attachments[]` / anexos — opcional;
- botão **Enviar solicitação**.

Texto sugerido para a tela:

```txt
Precisa de ajuda?

Descreva o que aconteceu e, se quiser, envie imagens ou PDFs que ajudem a explicar o problema.
Nossa equipe vai analisar sua solicitação e responder por e-mail.
```

Mensagem após envio com sucesso:

```txt
Recebemos sua solicitação de suporte.
Nossa equipe vai analisar as informações e responder por e-mail assim que possível.
```

Mensagem de erro genérica:

```txt
Não foi possível enviar sua solicitação agora. Tente novamente em instantes.
```

---

## Validações do frontend

Validar antes de enviar:

- assunto obrigatório;
- descrição obrigatória;
- limite de anexos;
- tamanho máximo por arquivo;
- tipos permitidos.

Configuração recomendada:

```txt
Máximo de anexos: 3
Tamanho máximo por anexo: 5 MB
```

Tipos permitidos:

```txt
image/png
image/jpeg
application/pdf
```

Extensões permitidas:

```txt
.png
.jpg
.jpeg
.pdf
```

Tipos bloqueados:

```txt
.exe
.bat
.cmd
.js
.zip
.rar
.7z
.scr
```

---

## Endpoint de backend

Criar endpoint autenticado para criação da solicitação.

Sugestão:

```http
POST /support/requests
Content-Type: multipart/form-data
```

Campos esperados:

```txt
subject: string
description: string
attachments[]: file[] opcional
```

O endpoint deve exigir usuária autenticada.

---

## Validações do backend

O backend deve validar novamente todos os limites, mesmo que o frontend já valide.

Validações obrigatórias:

- usuário autenticado;
- assunto não vazio;
- descrição não vazia;
- quantidade máxima de anexos;
- tamanho máximo por arquivo;
- MIME type permitido;
- extensão permitida;
- rejeitar arquivos executáveis ou potencialmente perigosos.

Caso a validação falhe, retornar erro adequado para o frontend.

---

## Banco de dados

Criar uma tabela simples para registrar a solicitação.

### Entidade sugerida: `SupportRequest`

Campos:

```txt
Id
UserId
UserName
UserEmail
Subject
Description
AttachmentCount
CreatedAt
```

Campo opcional:

```txt
Status
```

Valor inicial recomendado:

```txt
received
```

### Sobre anexos

Os anexos **não devem ser salvos no banco de dados**.

Opcionalmente, salvar apenas metadados:

```txt
SupportRequestAttachmentMetadata
```

Campos possíveis:

```txt
Id
SupportRequestId
FileName
ContentType
SizeBytes
CreatedAt
```

Mesmo nesse caso, salvar apenas metadados. Não salvar o conteúdo binário.

---

## Envio de e-mails com Resend

Ao criar uma solicitação com sucesso, o backend deve enviar dois e-mails via Resend.

---

### E-mail 1 — aviso para suporte

Enviado para:

```txt
lumasuporte.ia@gmail.com
```

Remetente:

```txt
Luma <noreply@ia-luma.com.br>
```

Assunto sugerido:

```txt
Nova solicitação de suporte #{{ SUPPORT_REQUEST_ID }}
```

Conteúdo esperado:

```txt
Nova solicitação de suporte recebida.

ID: {{ SUPPORT_REQUEST_ID }}
Usuária: {{ USER_NAME }}
E-mail: {{ USER_EMAIL }}
Assunto: {{ SUBJECT }}
Data: {{ CREATED_AT }}

Descrição:
{{ DESCRIPTION }}

Anexos recebidos: {{ ATTACHMENT_COUNT }}
```

Este e-mail deve incluir os anexos enviados pela usuária, respeitando os limites configurados.

#### Reply-To do e-mail para suporte

O e-mail enviado para `lumasuporte.ia@gmail.com` deve preferencialmente usar:

```txt
Reply-To: e-mail da usuária
```

Assim, quando alguém abrir `lumasuporte.ia@gmail.com` e clicar em **Responder**, a resposta vai diretamente para a usuária.

---

### E-mail 2 — confirmação para a usuária

Enviado para o e-mail da usuária autenticada.

Remetente:

```txt
Luma <noreply@ia-luma.com.br>
```

Assunto sugerido:

```txt
Recebemos sua solicitação de suporte
```

Conteúdo esperado:

```txt
Olá, {{ USER_NAME }}.

Recebemos sua solicitação de suporte.

ID da solicitação: {{ SUPPORT_REQUEST_ID }}
Assunto: {{ SUBJECT }}
Data: {{ CREATED_AT }}

Nossa equipe vai analisar as informações e responder por e-mail assim que possível.
```

Este e-mail **não precisa reenviar os anexos** para a usuária.

---

## Templates do Resend

Criar dois templates no painel do Resend.

---

### Template 1 — `support_request_admin_email`

Usado para enviar a solicitação para `lumasuporte.ia@gmail.com`.

Variáveis:

```txt
USER_NAME
USER_EMAIL
SUPPORT_REQUEST_ID
SUBJECT
DESCRIPTION
CREATED_AT
ATTACHMENT_COUNT
```

Uso:

- destinatário: `Email__SupportTo`;
- `Reply-To`: e-mail da usuária;
- anexos: sim.

---

### Template 2 — `support_request_user_confirmation_email`

Usado para confirmar o recebimento para a usuária.

Variáveis:

```txt
USER_NAME
SUPPORT_REQUEST_ID
SUBJECT
CREATED_AT
```

Uso:

- destinatário: e-mail da usuária;
- anexos: não.

---

## Variáveis de ambiente

Adicionar no `.env.production`:

```env
Resend__ApiKey=re_xxxxxxxxx

Email__From=Luma <noreply@ia-luma.com.br>
Email__SupportTo=lumasuporte.ia@gmail.com

Email__Templates__SupportAdmin=support_request_admin_email_ou_id
Email__Templates__SupportUserConfirmation=support_request_user_confirmation_email_ou_id

Email__MaxSupportAttachments=3
Email__MaxSupportAttachmentBytes=5242880
```

Observações:

- `Email__SupportTo` é a caixa que receberá os pedidos de suporte.
- `Email__MaxSupportAttachmentBytes=5242880` equivale a 5 MB por arquivo.
- O `Reply-To` do e-mail enviado ao suporte deve ser definido dinamicamente como o e-mail da usuária.
- Não usar `suporte@ia-luma.com.br` neste momento.

---

## Configuração no backend

Criar ou estender o serviço de e-mails existente para suportar os novos envios.

Sugestão de métodos:

```csharp
Task SendSupportRequestToAdminAsync(SupportRequest request, IReadOnlyList<EmailAttachment> attachments);
Task SendSupportRequestConfirmationToUserAsync(SupportRequest request);
```

Criar estrutura para anexos em memória:

```csharp
public sealed class EmailAttachment
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public byte[] Content { get; init; } = Array.Empty<byte>();
}
```

O conteúdo do arquivo deve ser usado apenas para envio do e-mail e descartado após a request.

---

## Regras para anexos

O backend deve rejeitar anexos que violem as regras.

Recomendação inicial:

```txt
Máximo de anexos: 3
Máximo por arquivo: 5 MB
Tipos permitidos: png, jpg, jpeg, pdf
```

Mensagens de erro sugeridas:

```txt
Você pode enviar no máximo 3 anexos.
```

```txt
Cada anexo deve ter no máximo 5 MB.
```

```txt
Formato de arquivo não permitido. Envie apenas PNG, JPG, JPEG ou PDF.
```

---

## Segurança

- Não confiar no MIME type informado pelo browser sem validação complementar.
- Validar extensão e content type.
- Limitar quantidade e tamanho total dos anexos.
- Não salvar binário dos anexos no banco.
- Não expor detalhes técnicos de erro para a usuária.
- Rate limit no endpoint de suporte para evitar abuso.

Rate limit sugerido:

```txt
3 solicitações por hora por usuária
```

ou, inicialmente:

```txt
5 solicitações por dia por usuária
```

---

## Frontend — experiência esperada

### Entrada no perfil

Adicionar card ou seção:

```txt
Precisa de ajuda?
Envie uma solicitação para nossa equipe de suporte.
```

Botão:

```txt
Abrir suporte
```

---

### Tela/modal de suporte

Campos:

```txt
Assunto
Descrição
Anexos opcionais
```

Botão:

```txt
Enviar solicitação
```

Estado de loading:

```txt
Enviando solicitação...
```

Mensagem de sucesso:

```txt
Recebemos sua solicitação. Nossa equipe vai responder por e-mail assim que possível.
```

---

## Testes recomendados

### Backend

Criar testes para:

- criação de solicitação válida;
- rejeição de assunto vazio;
- rejeição de descrição vazia;
- rejeição por excesso de anexos;
- rejeição por arquivo acima do limite;
- rejeição por tipo inválido;
- envio de e-mail para suporte;
- envio de confirmação para usuária;
- anexos presentes apenas no e-mail do suporte;
- anexos ausentes no e-mail de confirmação da usuária.

### Frontend

Testar:

- renderização da tela de suporte;
- validação de campos obrigatórios;
- validação de anexos;
- envio com sucesso;
- estado de loading;
- mensagem de erro.

---

## Critérios de aceite

- A usuária consegue acessar a tela de suporte pelo perfil.
- A usuária consegue enviar uma solicitação com assunto e descrição.
- A usuária consegue anexar arquivos permitidos dentro do limite configurado.
- O backend rejeita anexos inválidos.
- O backend salva uma solicitação básica no banco.
- O backend não salva o binário dos anexos no banco.
- O Resend envia e-mail para `lumasuporte.ia@gmail.com` com os dados da solicitação e anexos.
- O Resend envia confirmação para a usuária sem anexos.
- O e-mail enviado para suporte usa `Reply-To` com o e-mail da usuária.
- O fluxo não depende de painel admin.
- O fluxo não depende de SMTP corporativo.
- O fluxo não usa `suporte@ia-luma.com.br`.

---

## Tutorial de configuração no Resend

1. Acessar o painel do Resend.
2. Garantir que o domínio `ia-luma.com.br` está verificado.
3. Criar template `support_request_admin_email`.
4. Adicionar as variáveis:

```txt
USER_NAME
USER_EMAIL
SUPPORT_REQUEST_ID
SUBJECT
DESCRIPTION
CREATED_AT
ATTACHMENT_COUNT
```

5. Publicar o template.
6. Copiar o ID ou alias do template.
7. Criar template `support_request_user_confirmation_email`.
8. Adicionar as variáveis:

```txt
USER_NAME
SUPPORT_REQUEST_ID
SUBJECT
CREATED_AT
```

9. Publicar o template.
10. Copiar o ID ou alias do template.
11. Atualizar `.env.production` com os IDs/aliases.
12. Rebuildar a API.

---

## Deploy

Após implementar e atualizar `.env.production`:

```bash
cd ~/Luma/whatsapp-app
docker compose --env-file .env.production config
docker compose --env-file .env.production up -d --build api web
```

Se também houver alteração em `docker-compose.yml`:

```bash
cd ~/Luma/whatsapp-app
docker compose --env-file .env.production config
docker compose --env-file .env.production up -d --build --force-recreate
```

Acompanhar logs:

```bash
docker logs -f luma-api
```

---

## Observação final

Este fluxo foi desenhado para ser simples e funcional no MVP.

Ele resolve o problema de suporte sem exigir:

- painel administrativo;
- caixa de e-mail corporativa paga;
- SMTP próprio;
- armazenamento de anexos;
- sistema completo de tickets.

No futuro, este fluxo pode evoluir para um sistema interno de tickets, reaproveitando a tabela `SupportRequest` como base.
