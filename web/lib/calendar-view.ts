import type { CycleCalendar } from "./luma-api";

export const calendarLegend = [
  { type: "period_start_recorded", label: "Início registrado" },
  { type: "period_day_recorded", label: "Menstruação registrada" },
  { type: "period_end_recorded", label: "Fim registrado" },
  { type: "period_start_predicted", label: "Previsão menstrual" },
  { type: "period_day_predicted", label: "Duração prevista" },
  { type: "fertile_window_estimated", label: "Janela fértil estimada" },
  { type: "ovulation_estimated", label: "Ovulação estimada" },
  { type: "sexual_activity_recorded", label: "Relação registrada" },
  { type: "pregnancy_week", label: "Gravidez" },
  { type: "estimated_due_date", label: "Semana prevista para parto" },
];

export function buildCalendarDays(calendar: CycleCalendar | null) {
  if (!calendar) return [];
  const [year, month] = calendar.month.split("-").map(Number);
  const offset = new Date(Date.UTC(year, month - 1, 1)).getUTCDay();
  const byDate = new Map(calendar.days.map(day => [day.date, day]));
  return Array.from({ length: 42 }, (_, index) => {
    const date = new Date(Date.UTC(year, month - 1, 1 - offset + index));
    const key = date.toISOString().slice(0, 10);
    return { date: key, inMonth: key.startsWith(calendar.month), items: byDate.get(key)?.items ?? [] };
  });
}

// Synthetic data shared by the landing preview and marketing renders, never user records.
export const calendarDemo: CycleCalendar = {
  month: "2026-11",
  summary: { lastPeriodDate: "2026-11-02", nextPeriodDate: "2026-11-30", activePregnancy: false },
  days: [
    { date: "2026-11-02", items: [{ type: "period_start_recorded", label: "Início registrado", isPrediction: false }] },
    ...[3, 4, 5].map(day => ({ date: `2026-11-${String(day).padStart(2, "0")}`, items: [{ type: "period_day_recorded", label: "Menstruação registrada", isPrediction: false }] })),
    { date: "2026-11-06", items: [{ type: "period_end_recorded", label: "Fim registrado", isPrediction: false }] },
    ...[11, 12, 13, 14, 15].map(day => ({ date: `2026-11-${day}`, items: [{ type: "fertile_window_estimated", label: "Janela fértil estimada", isPrediction: true }] })),
    { date: "2026-11-16", items: [{ type: "ovulation_estimated", label: "Ovulação estimada", isPrediction: true }] },
    { date: "2026-11-20", items: [{ type: "sexual_activity_recorded", label: "Relação registrada", isPrediction: false }] },
    { date: "2026-11-30", items: [{ type: "period_start_predicted", label: "Previsão menstrual", isPrediction: true }] },
  ],
};
