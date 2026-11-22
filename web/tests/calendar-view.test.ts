import assert from "node:assert/strict";
import test from "node:test";
import { buildCalendarDays, calendarDemo, calendarLegend } from "../lib/calendar-view.ts";

test("calendar includes all six weeks and aligns November with Sunday", () => {
  const days = buildCalendarDays(calendarDemo);
  assert.equal(days.length, 42);
  assert.equal(days[0].date, "2026-11-01");
  assert.equal(days[29].date, "2026-11-30");
  assert.equal(days[30].inMonth, false);
  assert.equal(days[1].items[0].type, "period_start_recorded");
});

test("demo distinguishes records from estimates using production event types", () => {
  const items = calendarDemo.days.flatMap(day => day.items);
  const allowed = new Set(calendarLegend.map(item => item.type));
  assert.ok(items.every(item => allowed.has(item.type)));
  assert.ok(items.filter(item => item.type.endsWith("recorded")).every(item => !item.isPrediction));
  assert.ok(items.filter(item => item.type.endsWith("estimated") || item.type.endsWith("predicted")).every(item => item.isPrediction));
  assert.equal(calendarDemo.summary.nextPeriodDate, "2026-11-30");
  assert.equal(calendarDemo.days.find(day => day.date === "2026-11-16")?.items[0].type, "ovulation_estimated");
});

test("empty calendar and other month offsets do not invent records", () => {
  assert.deepEqual(buildCalendarDays(null), []);
  const days = buildCalendarDays({ ...calendarDemo, month: "2026-05", days: [] });
  assert.equal(days[0].date, "2026-04-26");
  assert.equal(days[5].date, "2026-05-01");
  assert.ok(days.every(day => day.items.length === 0));
});
