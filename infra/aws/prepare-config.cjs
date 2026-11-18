const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const compose = (file) => JSON.parse(execFileSync('docker', ['compose', '--env-file', path.join(root, 'whatsapp-app/.env'),
  '--profile', 'twilio-sandbox', '-f', file, 'config', '--format', 'json'], { cwd: path.join(root, 'whatsapp-app'), encoding: 'utf8' }));
const base = compose(path.join(root, 'whatsapp-app/docker-compose.yml'));
const production = compose(path.join(__dirname, 'config-input.json')).services.input.environment;
for (const environment of ['dev', 'production']) {
  const output = path.join(root, '.backups', `aws-${environment}-secret.json`);
  if (fs.existsSync(output)) throw new Error('Refusing to overwrite generated encryption keys; update the existing secret intentionally.');
  const api = { ...base.services[environment === 'dev' ? 'api-sandbox' : 'api'].environment };
  const domain = environment === 'dev' ? 'https://dev.ia-luma.com.br' : 'https://ia-luma.com.br';
  const postgresPassword = crypto.randomBytes(32).toString('hex');
  const redisPassword = crypto.randomBytes(32).toString('hex');
  if (environment === 'production') Object.assign(api, production);
  else {
    api.Privacy__EncryptionKey = crypto.randomBytes(32).toString('base64');
    api.Privacy__LookupPepper = crypto.randomBytes(32).toString('base64');
    api.Privacy__ActiveKeyId = 'aws-dev-v1';
  }
  Object.assign(api, {
    Deployment__Environment: environment,
    Twilio__VerifyDevelopmentMode: String(environment === 'dev'),
    ASPNETCORE_ENVIRONMENT: 'Production',
    ConnectionStrings__DefaultConnection: `Host=postgres;Port=5432;Database=luma;Username=luma;Password=${postgresPassword}`,
    Redis__ConnectionString: `redis:6379,password=${redisPassword}`,
    Luma__JwtSigningKey: crypto.randomBytes(48).toString('base64'),
    Luma__WebBaseUrl: domain,
    Luma__RequireActiveSubscription: 'true',
    Luma__StoreMessageBodies: 'false',
    Email__WebBaseUrl: domain,
    Rag__AllowEmbeddingGeneration: 'false',
    Notifications__WorkerEnabled: 'false',
    Twilio__VerifyChannel: 'sms',
    Twilio__NotificationStatusCallbackBaseUrl: (environment === 'production' ? 'https://api.ia-luma.com.br' : domain) + '/webhooks/twilio/notifications/status',
    DOTNET_GCHeapHardLimit: '0xC000000'
  });
  if (environment === 'dev') {
    api.Resend__ApiKey = '';
    api.Twilio__VerifyServiceSid = '';
  }
  const web = {
    LUMA_SITE_URL: domain,
    LUMA_API_BASE_URL: 'http://api:8080',
    LUMA_COOKIE_SECURE: 'true',
    LUMA_WHATSAPP_NUMBER: api.Twilio__WhatsAppFrom.replace(/^whatsapp:/, ''),
    NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY: api.Stripe__PublishableKey,
    NEXT_PUBLIC_BASE_URL: domain,
    NODE_OPTIONS: '--max-old-space-size=192'
  };
  const config = { environment, api, web, postgres: { POSTGRES_DB: 'luma', POSTGRES_USER: 'luma', POSTGRES_PASSWORD: postgresPassword }, redisPassword };
  fs.writeFileSync(output, JSON.stringify(config), { mode: 0o600 });
  console.log(JSON.stringify({ environment, saved: true, stripeMode: api.Stripe__SecretKey.startsWith('rk_test_') ? 'test' : 'live' }));
}
