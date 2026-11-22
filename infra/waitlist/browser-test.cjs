const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const root = path.resolve(__dirname, '../..');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.join(root,'.backups/social-tools/node_modules/playwright'));
const out = path.join(root,'.backups/waitlist-build/out');
const types={'.html':'text/html','.js':'application/javascript','.css':'text/css','.png':'image/png','.woff2':'font/woff2','.ico':'image/x-icon'};

async function main() {
  const {default:worker}=await import(pathToFileURL(path.join(__dirname,'worker.mjs')));
  const inserts=[];
  const env={API_SECRET:'local-test',ENCRYPTION_KEY:Buffer.alloc(32,1).toString('base64'),ASSETS:{},DB:{prepare(sql){return{bind(...args){return{
    async run(){if(sql.startsWith('INSERT INTO leads'))inserts.push(args);},async first(){return{attempts:1};}
  };}};}}};
  const server=http.createServer(async(req,res)=>{
    try {
      const url=new URL(req.url,'http://'+req.headers.host);
      if(url.pathname==='/api/waitlist'){
        const chunks=[];for await(const chunk of req)chunks.push(chunk);
        const response=await worker.fetch(new Request(url,{method:req.method,headers:req.headers,body:Buffer.concat(chunks)}),env);
        res.writeHead(response.status,Object.fromEntries(response.headers));res.end(Buffer.from(await response.arrayBuffer()));return;
      }
      let file=path.resolve(out,'.'+decodeURIComponent(url.pathname));
      if(!file.startsWith(out+path.sep)&&file!==out){res.writeHead(403);res.end();return;}
      if(fs.existsSync(file)&&fs.statSync(file).isDirectory())file=path.join(file,'index.html');
      else if(!fs.existsSync(file)&&fs.existsSync(file+'.html'))file+='.html';
      if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
      res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream'});fs.createReadStream(file).pipe(res);
    }catch(e){res.writeHead(500);res.end(e.message);}
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({channel:process.env.BROWSER_CHANNEL||'chrome',headless:true});
  try {
    for(const width of [390,1440]){
      for(const channel of ['email','whatsapp','both']){
        const page=await browser.newPage({viewport:{width,height:1000}});
        const errors=[];page.on('pageerror',e=>errors.push(e.message));
        await page.goto(`http://127.0.0.1:${server.address().port}/#lista`);
        const form=page.locator('.waitlist-form');await form.waitFor();
        assert.equal(await form.locator('input[type=checkbox]').count(),2);
        assert.equal(await form.locator('input[type=checkbox]:checked').count(),0);
        await form.locator(`input[value="${channel}"]`).check();
        assert.equal(await form.locator('input[name=email]').count(),channel==='whatsapp'?0:1);
        assert.equal(await form.locator('input[name=whatsapp]').count(),channel==='email'?0:1);
        await form.locator('input[name=name]').fill('Teste automatizado');
        if(channel!=='whatsapp')await form.locator('input[name=email]').fill(`qa-${width}-${channel}@example.com`);
        if(channel!=='email')await form.locator('input[name=whatsapp]').fill('11912345678');
        const before=inserts.length;
        await form.locator('button[type=submit]').click();
        assert.equal(inserts.length,before);
        await form.locator('input[name=termsAccepted]').check();
        await form.locator('button[type=submit]').click();
        assert.equal(inserts.length,before);
        await form.locator('input[name=launchConsent]').check();
        await page.evaluate(()=>document.fonts.ready);
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
        if(channel==='both')await page.locator('#lista').screenshot({path:path.join(root,`.backups/waitlist-${width}.png`)});
        const responsePromise=page.waitForResponse(r=>r.url().endsWith('/api/waitlist'));
        await form.locator('button[type=submit]').click();
        assert.equal((await responsePromise).status(),200);
        await page.getByRole('status').filter({hasText:'canais escolhidos'}).waitFor();
        assert.equal(inserts.length,before+1);assert.deepEqual(errors,[]);
        console.log(`${width}px / ${channel}: two independent consents, selected contacts only, encrypted persistence, no overflow or browser errors`);
        await page.close();
      }
    }
  } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
}
main().catch(e=>{console.error(e);process.exitCode=1;});
