const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const stage = path.join(root, '.backups/waitlist-build');
if (!stage.startsWith(path.join(root, '.backups') + path.sep)) throw new Error('Invalid build path');
fs.rmSync(stage, { recursive: true, force: true });
fs.mkdirSync(stage, { recursive: true });
const source = path.join(root, 'web');
for (const name of ['app', 'components', 'lib', 'public', 'package.json', 'tsconfig.json', 'next-env.d.ts', 'postcss.config.mjs']) {
  if (fs.existsSync(path.join(source, name))) fs.cpSync(path.join(source, name), path.join(stage, name), { recursive: true });
}
fs.symlinkSync(path.join(source, 'node_modules'), path.join(stage, 'node_modules'), 'junction');
const allowed = new Set(['page.tsx', 'layout.tsx', 'globals.css', 'termos', 'privacidade', 'icon.tsx', 'apple-icon.tsx', 'opengraph-image.tsx', 'robots.ts', 'sitemap.ts']);
for (const name of fs.readdirSync(path.join(stage, 'app'))) if (!allowed.has(name)) fs.rmSync(path.join(stage, 'app', name), { force: true, recursive: true });
const layout = path.join(stage, 'app/layout.tsx');
fs.writeFileSync(layout, fs.readFileSync(layout, 'utf8').replace('export const dynamic = "force-dynamic";', 'export const dynamic = "force-static";'));
for (const name of ['robots.ts', 'sitemap.ts']) {
  const file = path.join(stage, 'app', name);
  fs.writeFileSync(file, fs.readFileSync(file, 'utf8').replace('export const dynamic = "force-dynamic";', 'export const dynamic = "force-static";'));
}
for (const name of ['icon.tsx', 'apple-icon.tsx', 'opengraph-image.tsx']) {
  const file = path.join(stage, 'app', name);
  if (fs.existsSync(file)) fs.appendFileSync(file, '\nexport const dynamic = "force-static";\n');
}
fs.writeFileSync(path.join(stage, 'next.config.mjs'), `export default { output: "export", images: { unoptimized: true }, turbopack: { root: ${JSON.stringify(root)} } };`);
execFileSync(process.execPath, [path.join(source, 'node_modules/next/dist/bin/next'), 'build'], { cwd: stage, stdio: 'inherit',
  env: { ...process.env, NEXT_PUBLIC_LUMA_SITE_MODE: 'waitlist', LUMA_SITE_URL: 'https://espera.ia-luma.com.br' } });
const out = path.join(stage, 'out');
fs.copyFileSync(path.join(__dirname, 'worker.mjs'), path.join(out, '_worker.js'));
fs.writeFileSync(path.join(out, '_routes.json'), JSON.stringify({ version: 1, include: ['/api/waitlist'], exclude: [] }));
fs.writeFileSync(path.join(out, '_headers'), '/*\n  X-Content-Type-Options: nosniff\n  Referrer-Policy: strict-origin-when-cross-origin\n');
console.log('Pages output: ' + out);
