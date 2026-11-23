import test from "node:test";
import assert from "node:assert/strict";
import { getPlanPrice, plans } from "../lib/luma-api.ts";

test("final offers distinguish annual upfront totals from monthly equivalents", () => {
  assert.equal(plans.basico.monthlyPrice, "R$ 32,90/mês");
  assert.equal(plans.basico.annualPrice, "R$ 334,80/ano");
  assert.equal(plans.basico.annualEquivalent, "equivale a R$ 27,90/mês");
  assert.equal(plans.essencial.monthlyPrice, "R$ 59,90/mês");
  assert.equal(plans.essencial.annualPrice, "R$ 658,80/ano");
  assert.equal(plans.essencial.annualEquivalent, "equivale a R$ 54,90/mês");
  assert.ok(plans.basico.benefits.some(value => value.includes("150 por mês")));
  assert.ok(plans.essencial.benefits.some(value => value.includes("300 por mês")));
});

test("existing subscription renders its contracted amount instead of new offer prices", () => {
  assert.match(getPlanPrice("basico", "monthly", 11), /11,00\/mês/);
  assert.match(getPlanPrice("essencial", "annual", 118.80), /118,80\/ano/);
  assert.equal(getPlanPrice("basico", "monthly"), "R$ 32,90/mês");
});
