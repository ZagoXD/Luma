const fs = require('node:fs');
const crypto = require('node:crypto');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const env = Object.fromEntries(fs.readFileSync(path.join(root, 'whatsapp-app/.env'), 'utf8').split(/\r?\n/).filter(l => /^\w+=/.test(l)).map(l => { const i=l.indexOf('='); return [l.slice(0,i),l.slice(i+1)]; }));
const input = process.argv[2];
if (!input) throw new Error('Usage: node infra/waitlist/export.cjs private-export.json > contacts.csv');
const data = JSON.parse(fs.readFileSync(input,'utf8'));
const rows = Array.isArray(data) ? data : data.result?.[0]?.results;
if (!rows) throw new Error('Expected an array or a D1 result envelope');
const escape = value => '"'+String(value ?? '').replaceAll('"','""')+'"';
console.log('name,email,created_at,consent_version');
for (const row of rows) {
  const bytes=Buffer.from(row.encrypted_contact,'base64');
  const decipher=crypto.createDecipheriv('aes-256-gcm',Buffer.from(env.WAITLIST_ENCRYPTION_KEY,'base64'),bytes.subarray(0,12));
  decipher.setAAD(Buffer.from('luma.waitlist.v1'));decipher.setAuthTag(bytes.subarray(-16));
  const contact=JSON.parse(Buffer.concat([decipher.update(bytes.subarray(12,-16)),decipher.final()]).toString('utf8'));
  console.log([contact.name,contact.email,row.created_at,row.consent_version].map(escape).join(','));
}
