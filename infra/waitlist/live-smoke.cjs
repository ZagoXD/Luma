const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..');

async function main(){
  const env=Object.fromEntries(fs.readFileSync(path.join(root,'whatsapp-app/.env'),'utf8').split(/\r?\n/).filter(l=>/^\w+=/.test(l)).map(l=>{const i=l.indexOf('=');return[l.slice(0,i),l.slice(i+1)];}));
  assert.ok(env.WAITLIST_ENCRYPTION_KEY,'Local waitlist key is required for isolated cleanup');
  const marker='waitlist-smoke-'+crypto.randomUUID();
  const cases=['email','whatsapp','both'].map((channel,i)=>({name:marker,channel,
    email:`${marker}-${i}@example.com`,whatsapp:`1191234567${i}`,termsAccepted:true,launchConsent:true,
    version:'2026-11-22',termsVersion:'2026-11-22-v2',privacyVersion:'2026-11-22-v2'}));
  const {validateWaitlist}=await import('../../web/lib/waitlist-validation.mjs');
  const hashes=cases.map(c=>{const v=validateWaitlist(c);assert.ok(v);return crypto.createHmac('sha256',env.WAITLIST_ENCRYPTION_KEY).update(v.email||`whatsapp:${v.whatsapp}`).digest('hex');});
  fs.writeFileSync(path.join(root,'.backups/waitlist-smoke.json'),JSON.stringify({marker,hashes,cases}));
  for(const body of [{...cases[0],launchConsent:false},...cases]){
    const response=await fetch('https://espera.ia-luma.com.br/api/waitlist',{method:'POST',headers:{'Content-Type':'application/json',Origin:'https://espera.ia-luma.com.br'},body:JSON.stringify(body)});
    assert.equal(response.status,body.launchConsent?200:400);
    assert.ok((await response.json()).message);console.log(`${body.launchConsent?body.channel:'missing consent'}: HTTP ${response.status}`);
  }
  console.log('Synthetic contact data only; no email, WhatsApp or SMS sends. Remove the isolated records after verification.');
}
main().catch(e=>{console.error(e);process.exitCode=1;});
