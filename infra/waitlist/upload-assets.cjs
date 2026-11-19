const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../..');
const out = path.join(root, '.backups/waitlist-build/out');
const token = process.env.CLOUDFLARE_PAGES_UPLOAD_TOKEN || fs.readFileSync(path.join(root, '.backups/pages-upload-token'), 'utf8').trim();
const mime = { '.html':'text/html; charset=utf-8', '.txt':'text/plain; charset=utf-8', '.js':'application/javascript', '.css':'text/css', '.json':'application/json', '.png':'image/png', '.jpg':'image/jpeg', '.jpeg':'image/jpeg', '.webp':'image/webp', '.woff2':'font/woff2', '.svg':'image/svg+xml', '.ico':'image/x-icon', '.xml':'application/xml' };
const manifest = {}, assets = [];
function walk(dir) {
  for (const name of fs.readdirSync(dir)) {
    const file=path.join(dir,name);
    if(fs.statSync(file).isDirectory()){walk(file);continue;}
    const relative=path.relative(out,file).replaceAll('\\','/');
    if (['_worker.js','_routes.json','_headers','_redirects'].includes(relative)) continue;
    const content=fs.readFileSync(file), key=crypto.createHash('sha256').update(content).update(path.extname(file)).digest('hex').slice(0,32);
    manifest['/'+relative]=key;
    assets.push({key,value:content.toString('base64'),metadata:{contentType:mime[path.extname(file)]||'application/octet-stream'},base64:true});
  }
}
(async()=>{
  walk(out);
  const response=await fetch('https://api.cloudflare.com/client/v4/pages/assets/check-missing',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:JSON.stringify({hashes:assets.map(x=>x.key)})});
  const missing=await response.json();if(!missing.success)throw new Error('Asset lookup failed: '+JSON.stringify(missing.errors));
  const needed=new Set(missing.result), uploads=assets.filter(x=>needed.has(x.key));
  for(let i=0;i<uploads.length;i+=10){const r=await fetch('https://api.cloudflare.com/client/v4/pages/assets/upload',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:JSON.stringify(uploads.slice(i,i+10))});const data=await r.json();if(!data.success)throw new Error('Asset upload failed: '+JSON.stringify(data.errors));}
  const r=await fetch('https://api.cloudflare.com/client/v4/pages/assets/upsert-hashes',{method:'POST',headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:JSON.stringify({hashes:assets.map(x=>x.key)})});if(!(await r.json()).success)throw new Error('Asset registration failed');
  fs.writeFileSync(path.join(root,'.backups/pages-manifest.json'),JSON.stringify(manifest));
  console.log(JSON.stringify({assets:assets.length,uploaded:uploads.length}));
})().catch(e=>{console.error(e.message);process.exit(1)});
