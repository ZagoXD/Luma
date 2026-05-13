# Task — E-mails transacionais com Resend e fluxo de recuperação de senha

## Contexto

O Luma já possui domínio próprio configurado em produção:

- Web produção: `https://ia-luma.com.br`
- API produção: `https://api.ia-luma.com.br`
- Web local: `http://localhost:3000`
- API local: conforme variável local do projeto

O Resend será usado para envio de e-mails automáticos do sistema usando um remetente padrão `noreply`, sem necessidade de caixa de entrada real para esse endereço.

Esta task contempla apenas:

1. E-mail automático de boas-vindas após cadastro.
2. E-mail automático de assinatura efetuada.
3. Fluxo completo de “esqueci minha senha”.
4. Templates correspondentes no Resend.

Não faz parte desta task qualquer sistema de suporte, tickets internos ou respostas manuais por e-mail.

---

## Objetivo

Implementar no Luma um serviço de e-mails transacionais usando Resend, com templates gerenciados no painel do Resend e variáveis configuráveis via `.env`, garantindo que links enviados por e-mail funcionem corretamente tanto em ambiente local quanto em produção.

---

## Variáveis de ambiente necessárias

Adicionar ao `.env`, `.env.production` ou equivalente:

```env
Resend__ApiKey=re_xxxxxxxxxxxxxxxxx

Email__From=Luma <noreply@ia-luma.com.br>
Email__PasswordResetExpirationMinutes=30

Email__Templates__Welcome=d0f00000-0000-0000-0000-000000000000
Email__Templates__SubscriptionCreated=d0f00000-0000-0000-0000-000000000000
Email__Templates__PasswordReset=d0f00000-0000-0000-0000-000000000000

LUMA_WEB_BASE_URL=https://ia-luma.com.br
```

Para desenvolvimento local:

```env
LUMA_WEB_BASE_URL=http://localhost:3000
```

Observação importante: o backend **não deve chumbar** URLs como `https://ia-luma.com.br` ou `http://localhost:3000` diretamente no código. Sempre deve montar links públicos a partir de `LUMA_WEB_BASE_URL`.

---

## Templates no Resend

Criar três templates no Resend.

### 1. Template: boas-vindas

Uso: enviado logo após o cadastro ser finalizado com sucesso.

Nome sugerido:

```txt
welcome_email
```

Assunto sugerido:

```txt
Bem-vinda à Luma 💜
```

Variáveis esperadas:

```txt
userName
loginUrl
```

Conteúdo sugerido:

```html
<p>Olá, {{ userName }}!</p>

<p>Parabéns, seu cadastro na Luma foi concluído com sucesso.</p>

<p>Agora você já pode acessar sua conta, configurar suas preferências e acompanhar seus lembretes.</p>

<p>
  <a href="{{ loginUrl }}">Acessar minha conta</a>
</p>

<p>Com carinho,<br />Equipe Luma</p>
```

Caso o nome da usuária não esteja disponível, o backend deve enviar um fallback como `"tudo bem?"`, `"Olá"` ou simplesmente omitir o nome dependendo da implementação do template.

---

### 2. Template: assinatura efetuada

Uso: enviado quando uma assinatura/plano for ativado com sucesso.

Nome sugerido:

```txt
subscription_created_email
```

Assunto sugerido:

```txt
Assinatura ativada com sucesso
```

Variáveis esperadas:

```txt
userName
planName
billingUrl
appUrl
```

Conteúdo sugerido:

```html
<p>Olá, {{ userName }}!</p>

<p>Sua assinatura foi ativada com sucesso.</p>

<p><strong>Plano:</strong> {{ planName }}</p>

<p>Você já pode aproveitar os recursos disponíveis no seu plano.</p>

<p>
  <a href="{{ appUrl }}">Acessar minha conta</a>
</p>

<p>Se quiser consultar detalhes de cobrança, acesse:</p>

<p>
  <a href="{{ billingUrl }}">Ver assinatura e cobrança</a>
</p>

<p>Com carinho,<br />Equipe Luma</p>
```

O envio deve ocorrer apenas após confirmação real da assinatura no backend, preferencialmente após evento confiável do Stripe ou ponto já consolidado do fluxo de assinatura.

---

### 3. Template: recuperação de senha

Uso: enviado quando a usuária solicitar recuperação de senha.

Nome sugerido:

```txt
password_reset_email
```

Assunto sugerido:

```txt
Redefinição de senha da sua conta Luma
```

Variáveis esperadas:

```txt
userName
resetUrl
expiresInMinutes
```

Conteúdo sugerido:

```html
<p>Olá, {{ userName }}!</p>

<p>Recebemos uma solicitação para redefinir a senha da sua conta Luma.</p>

<p>Para criar uma nova senha, clique no botão abaixo:</p>

<p>
  <a href="{{ resetUrl }}">Redefinir minha senha</a>
</p>

<p>Este link expira em {{ expiresInMinutes }} minutos.</p>

<p>Se você não solicitou essa alteração, ignore este e-mail. Sua senha atual continuará válida.</p>

<p>Com carinho,<br />Equipe Luma</p>
```

---

## Backend — Serviço de e-mail

Criar uma abstração para envio de e-mails, evitando chamadas diretas ao Resend espalhadas pelo código.

Sugestão de estrutura:

```txt
src/Luma.Api/Services/Email/
├── IEmailService.cs
├── ResendEmailService.cs
├── EmailOptions.cs
└── EmailTemplateOptions.cs
```

### Interface sugerida

```csharp
public interface IEmailService
{
    Task SendWelcomeEmailAsync(string to, string? userName, CancellationToken cancellationToken = default);

    Task SendSubscriptionCreatedEmailAsync(
        string to,
        string? userName,
        string planName,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetEmailAsync(
        string to,
        string? userName,
        string resetUrl,
        int expiresInMinutes,
        CancellationToken cancellationToken = default);
}
```

### Configurações sugeridas

```csharp
public sealed class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class EmailOptions
{
    public string From { get; set; } = "Luma <noreply@ia-luma.com.br>";
    public int PasswordResetExpirationMinutes { get; set; } = 30;
}

public sealed class EmailTemplateOptions
{
    public string Welcome { get; set; } = string.Empty;
    public string SubscriptionCreated { get; set; } = string.Empty;
    public string PasswordReset { get; set; } = string.Empty;
}
```

No `Program.cs`, registrar as configurações usando o padrão hierárquico do .NET:

```csharp
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection("Resend"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<EmailTemplateOptions>(builder.Configuration.GetSection("Email:Templates"));

builder.Services.AddHttpClient<IEmailService, ResendEmailService>();
```

---

## Backend — Fluxo de boas-vindas

### Quando enviar

Enviar após o cadastro ser concluído com sucesso e o usuário ser persistido no banco.

### Comportamento esperado

1. Usuária finaliza cadastro.
2. Backend cria a conta normalmente.
3. Backend chama `SendWelcomeEmailAsync`.
4. O e-mail é enviado para o endereço cadastrado.
5. Falha no envio do e-mail **não deve desfazer o cadastro**.
6. Em caso de falha, registrar log para investigação.

### Link usado

O `loginUrl` enviado ao template deve ser montado com:

```txt
{LUMA_WEB_BASE_URL}/login
```

Exemplo produção:

```txt
https://ia-luma.com.br/login
```

Exemplo local:

```txt
http://localhost:3000/login
```

---

## Backend — Fluxo de assinatura efetuada

### Quando enviar

Enviar quando o backend tiver certeza de que a assinatura foi criada/ativada com sucesso.

Possíveis pontos de disparo:

- Após processamento de webhook confiável do Stripe.
- Após atualização interna do status da assinatura para ativo.

Evitar envio apenas com base em uma navegação do frontend, para não disparar e-mails sem confirmação real.

### Comportamento esperado

1. Assinatura é confirmada no backend.
2. Backend identifica usuário, e-mail e plano.
3. Backend chama `SendSubscriptionCreatedEmailAsync`.
4. E-mail é enviado informando o plano ativado.
5. Falha no envio não deve desfazer a assinatura.
6. Registrar log de sucesso/falha.

### Links usados

```txt
appUrl = {LUMA_WEB_BASE_URL}/profile
billingUrl = {LUMA_WEB_BASE_URL}/profile?tab=billing
```

A rota final pode ser ajustada conforme a navegação real do frontend.

---

## Backend — Fluxo de esqueci minha senha

## Endpoints necessários

Criar endpoints:

```txt
POST /auth/forgot-password
POST /auth/reset-password
```

Ou seguir o padrão de rotas já existente no backend, desde que a responsabilidade fique clara.

---

### Endpoint: solicitar recuperação

```txt
POST /auth/forgot-password
```

Payload:

```json
{
  "email": "usuario@email.com"
}
```

Resposta sempre genérica:

```json
{
  "message": "Se existir uma conta vinculada a este e-mail, enviaremos instruções para redefinir sua senha."
}
```

Essa resposta deve ser igual tanto para e-mails existentes quanto inexistentes, para evitar enumeração de usuários.

### Regras

1. Validar formato do e-mail.
2. Procurar usuário pelo e-mail.
3. Se não existir, retornar resposta genérica mesmo assim.
4. Se existir, gerar token seguro.
5. Salvar apenas o hash do token no banco.
6. Definir expiração, por exemplo 30 minutos.
7. Enviar e-mail com link de recuperação.
8. Retornar resposta genérica.

---

## Tabela de token de recuperação

Criar entidade/tabela para tokens de recuperação de senha.

Nome sugerido:

```txt
PasswordResetToken
```

Campos sugeridos:

```txt
Id
UserId
TokenHash
ExpiresAt
UsedAt
CreatedAt
RequestIp, opcional
UserAgent, opcional
```

Regras:

- O token puro nunca deve ser salvo no banco.
- Salvar apenas hash do token.
- Token deve expirar.
- Token deve ser de uso único.
- Ao usar token com sucesso, preencher `UsedAt`.
- Opcionalmente invalidar tokens antigos do mesmo usuário ao gerar um novo.

---

## Geração do link de reset

O backend deve montar a URL usando `LUMA_WEB_BASE_URL`.

Formato sugerido:

```txt
{LUMA_WEB_BASE_URL}/reset-password?token={token}
```

Exemplo produção:

```txt
https://ia-luma.com.br/reset-password?token=abc123
```

Exemplo local:

```txt
http://localhost:3000/reset-password?token=abc123
```

Não chumbar domínio de produção no código.

---

### Endpoint: redefinir senha

```txt
POST /auth/reset-password
```

Payload:

```json
{
  "token": "token-recebido-no-email",
  "newPassword": "novaSenhaSegura"
}
```

Regras:

1. Validar token informado.
2. Calcular hash do token recebido.
3. Buscar token no banco por hash.
4. Validar se existe.
5. Validar se não expirou.
6. Validar se ainda não foi usado.
7. Validar força mínima da nova senha.
8. Atualizar senha do usuário.
9. Marcar token como usado.
10. Retornar sucesso.

Resposta sugerida:

```json
{
  "message": "Senha redefinida com sucesso."
}
```

Em caso de token inválido, expirado ou usado:

```json
{
  "message": "Link de recuperação inválido ou expirado."
}
```

---

## Frontend — Login

Na tela de login, adicionar um botão/link abaixo do formulário:

```txt
Esqueci minha senha
```

Esse botão deve levar para uma tela específica de recuperação.

Sugestão de rota:

```txt
/forgot-password
```

---

## Frontend — Tela de solicitar recuperação

Criar tela:

```txt
/forgot-password
```

### UI esperada

Campos:

```txt
E-mail
```

Ação:

```txt
Enviar instruções
```

Após submit, exibir mensagem genérica:

```txt
Se existir uma conta vinculada a este e-mail, enviaremos instruções para redefinir sua senha.
```

Essa mensagem deve aparecer independente de o e-mail existir ou não.

---

## Frontend — Tela de redefinir senha

Criar tela:

```txt
/reset-password?token=...
```

### UI esperada

Campos:

```txt
Nova senha
Confirmar nova senha
```

Ação:

```txt
Redefinir senha
```

Validações:

- Senha obrigatória.
- Confirmação deve bater com a senha.
- Aplicar regras mínimas de senha do backend também no frontend quando possível.

Após sucesso:

```txt
Sua senha foi redefinida com sucesso. Você já pode fazer login.
```

Adicionar botão/link:

```txt
Ir para login
```

---

## Segurança e boas práticas

- Nunca retornar se o e-mail existe ou não no fluxo de esqueci minha senha.
- Nunca salvar token puro no banco.
- Token deve ser forte e aleatório.
- Token deve expirar.
- Token deve ser de uso único.
- Aplicar rate limit no endpoint de forgot password.
- Registrar falhas de envio de e-mail sem expor detalhes ao usuário final.
- Não quebrar cadastro/assinatura caso o envio de e-mail falhe.
- Usar `LUMA_WEB_BASE_URL` para todos os links públicos.
- Não commitar `Resend__ApiKey` no Git.

---

## Logs recomendados

Criar logs para:

```txt
welcome_email_requested
welcome_email_sent
welcome_email_failed
subscription_created_email_requested
subscription_created_email_sent
subscription_created_email_failed
password_reset_requested
password_reset_email_sent
password_reset_email_failed
password_reset_completed
password_reset_failed
```

Opcionalmente criar tabela de log de e-mails:

```txt
EmailLog
```

Campos sugeridos:

```txt
Id
To
Subject
TemplateId
Provider
ProviderMessageId
Status
Error
CreatedAt
```

---

## Tutorial — Configuração no Resend

### 1. Criar conta e API Key

1. Entrar no Resend.
2. Ir em `API Keys`.
3. Criar uma API key para o projeto.
4. Copiar a chave.
5. Adicionar no `.env.production`:

```env
Resend__ApiKey=re_xxxxxxxxxxxxxxxxx
```

---

### 2. Adicionar domínio

1. Ir em `Domains`.
2. Clicar em `Add Domain`.
3. Adicionar:

```txt
ia-luma.com.br
```

4. Selecionar região apropriada, se solicitado. Exemplo usado:

```txt
São Paulo / sa-east-1
```

5. Copiar os registros DNS exigidos.

---

### 3. Configurar DNS no Cloudflare

No Cloudflare, em `ia-luma.com.br > DNS > Records`, adicionar os registros fornecidos pelo Resend.

Exemplo do domínio atual:

```txt
TXT   resend._domainkey   p=...
MX    send                feedback-smtp.sa-east-1.amazonses.com   priority 10
TXT   send                v=spf1 include:amazonses.com ~all
TXT   _dmarc              v=DMARC1; p=none;
```

Depois voltar ao Resend e clicar em verificar DNS.

Status esperado:

```txt
Domain verified: Your domain is ready to send emails.
```

---

### 4. Criar templates no Resend

Criar os templates:

```txt
welcome_email
subscription_created_email
password_reset_email
```

Para cada template:

1. Criar template no painel do Resend.
2. Definir assunto.
3. Definir HTML.
4. Inserir variáveis conforme listado nesta task.
5. Salvar.
6. Copiar o ID ou alias do template.
7. Adicionar no `.env.production`:

```env
Email__Templates__Welcome=...
Email__Templates__SubscriptionCreated=...
Email__Templates__PasswordReset=...
```

---

## Variáveis finais esperadas

Produção:

```env
Resend__ApiKey=re_xxxxxxxxxxxxxxxxx

Email__From=Luma <noreply@ia-luma.com.br>
Email__PasswordResetExpirationMinutes=30

Email__Templates__Welcome=...
Email__Templates__SubscriptionCreated=...
Email__Templates__PasswordReset=...

LUMA_WEB_BASE_URL=https://ia-luma.com.br
```

Local:

```env
Resend__ApiKey=re_xxxxxxxxxxxxxxxxx

Email__From=Luma <noreply@ia-luma.com.br>
Email__PasswordResetExpirationMinutes=30

Email__Templates__Welcome=...
Email__Templates__SubscriptionCreated=...
Email__Templates__PasswordReset=...

LUMA_WEB_BASE_URL=http://localhost:3000
```

---

## Critérios de aceite

- Após cadastro, usuário recebe e-mail de boas-vindas.
- Após assinatura confirmada, usuário recebe e-mail de assinatura efetuada.
- Tela de login possui botão/link “Esqueci minha senha”.
- Tela de forgot password permite informar e-mail.
- Após solicitar recuperação, frontend sempre exibe mensagem genérica.
- Se e-mail existir, backend envia e-mail de recuperação com link válido.
- Link de recuperação usa `LUMA_WEB_BASE_URL`.
- Tela de reset password permite definir nova senha.
- Token de recuperação expira.
- Token de recuperação é de uso único.
- Backend não salva token puro no banco.
- Nenhuma rota pública revela se um e-mail está cadastrado.
- Resend usa remetente `Luma <noreply@ia-luma.com.br>`.
- Código não contém domínio de produção chumbado.
