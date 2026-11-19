import test from "node:test";
import assert from "node:assert/strict";
import { sessionCookies, trustedMutation, cookieNames, safeReturnPath } from "../lib/session-policy.ts";

test("access and refresh cookies are host-only HttpOnly, secure and have distinct bounded lifetimes", () => {
  const now=Date.parse("2026-11-19T12:00:00Z");
  const cookies=sessionCookies({token:"access",refreshToken:"refresh",accessExpiresAt:"2026-11-19T12:10:00Z",refreshExpiresAt:"2026-12-19T12:00:00Z"},true,now);
  assert.equal(cookies.length,2);
  assert.equal(cookies[0].name,"__Host-luma_access");
  assert.equal(cookies[1].name,"__Host-luma_refresh");
  assert.equal(cookies[0].options.maxAge,600);
  assert.equal(cookies[1].options.maxAge,30*86400);
  for(const cookie of cookies){assert.equal(cookie.options.httpOnly,true);assert.equal(cookie.options.secure,true);assert.equal(cookie.options.path,"/");assert.equal(cookie.options.sameSite,"lax");assert.equal("domain" in cookie.options,false);}
  assert.deepEqual(cookieNames(false),{access:"luma_access",refresh:"luma_refresh"});
});

test("unsafe browser requests require an exact same-origin HTTPS source",()=>{
  assert.equal(trustedMutation("POST","ia-luma.com.br","https://ia-luma.com.br","same-origin",true),true);
  for(const origin of [null,"https://evil.example","https://dev.ia-luma.com.br","http://ia-luma.com.br","null"])
    assert.equal(trustedMutation("POST","ia-luma.com.br",origin,"cross-site",true),false);
  assert.equal(trustedMutation("POST","ia-luma.com.br","https://ia-luma.com.br","cross-site",true),false);
  assert.equal(trustedMutation("POST","localhost:3000","http://localhost:3000","same-origin",false),true);
  assert.equal(trustedMutation("GET","ia-luma.com.br",null,null,true),true);
});

test("post-login navigation rejects external and executable redirect values",()=>{
  for(const value of ["https://evil.example", "//evil.example", "javascript:alert(1)", "/\\evil.example", "/\n/evil.example", "/%2f%2fevil.example", null])
    assert.equal(safeReturnPath(value),"/perfil");
  assert.equal(safeReturnPath("/checkout/essencial?billing=monthly"),"/checkout/essencial?billing=monthly");
});
