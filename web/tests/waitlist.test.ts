import { test } from "node:test";
import assert from "node:assert/strict";
import { validateWaitlist } from "../lib/waitlist.ts";
const valid = { name: " Nay ", email: " NAY@EXAMPLE.COM ", channel: "email", termsAccepted: true,
  launchConsent: true, version: "2026-11-22", termsVersion: "2026-11-22", privacyVersion: "2026-11-22" };

test("waitlist requires separate explicit legal acceptance and launch authorization", () => {
  for (const input of [{...valid, termsAccepted:false}, {...valid, launchConsent:false}, {...valid, termsAccepted:"true"},
    {name:"Nay",email:"nay@example.com",consent:true}, {...valid,version:"2026-11-18"}, {...valid,termsVersion:"old"}, {...valid,privacyVersion:"old"}])
    assert.equal(validateWaitlist(input),null);
});
test("email-only normalizes email and discards unselected phone", () => {
  const result=validateWaitlist({...valid,whatsapp:"invalid unselected data"});
  assert.equal(result?.name,"Nay"); assert.equal(result?.email,"nay@example.com");
  assert.equal(result?.whatsapp,null); assert.equal(result?.emailOptIn,true); assert.equal(result?.whatsappOptIn,false);
  for(const input of [{...valid,email:"invalid"},{...valid,name:"N".repeat(101)},{...valid,channel:"sms"}]) assert.equal(validateWaitlist(input),null);
});
test("WhatsApp-only accepts Brazilian mobile with or without country code", () => {
  for(const whatsapp of ["(16) 99233-0309","+55 16 99233-0309","5516992330309"]) {
    const result=validateWaitlist({...valid,channel:"whatsapp",email:undefined,whatsapp});
    assert.equal(result?.whatsapp,"+5516992330309"); assert.equal(result?.email,null);
    assert.equal(result?.emailOptIn,false); assert.equal(result?.whatsappOptIn,true);
  }
  for(const whatsapp of ["123","(20) 99233-0309","(16) 3333-0309","+1 415 523-8886","(16) 99999-9999"])
    assert.equal(validateWaitlist({...valid,channel:"whatsapp",whatsapp}),null);
});
test("both channels require both valid contacts", () => {
  const both={...valid,channel:"both",whatsapp:"16992330309"};
  assert.equal(validateWaitlist(both)?.whatsappOptIn,true); assert.equal(validateWaitlist(both)?.emailOptIn,true);
  assert.equal(validateWaitlist({...both,email:""}),null); assert.equal(validateWaitlist({...both,whatsapp:""}),null);
});
test("invalid payload shapes are rejected", () => {
  for(const value of [null,[],"text",42]) assert.equal(validateWaitlist(value as never),null);
});
