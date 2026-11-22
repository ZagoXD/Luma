import type { CycleCalendar } from "../../lib/luma-api";
import { buildCalendarDays, calendarLegend } from "../../lib/calendar-view";

export function CycleCalendarGrid({ calendar, label, legend = "all" }: {
  calendar: CycleCalendar;
  label?: string;
  legend?: "all" | "visible";
}) {
  const types = new Set(calendar.days.flatMap(day => day.items.map(item => item.type)));
  const entries = calendarLegend.filter(item => legend === "all" || types.has(item.type));
  const days = buildCalendarDays(calendar);
  return (
    <div className="luma-month">
      <div className="luma-month-grid" role="grid" aria-label={label ?? `Calendário de ${calendar.month}`}>
        <div role="row" className="luma-month-week">
          {["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"].map(day => <div role="columnheader" key={day}>{day}</div>)}
        </div>
        {Array.from({ length: 6 }, (_, row) => (
          <div role="row" className="luma-month-week" key={row}>
            {days.slice(row * 7, row * 7 + 7).map(day => (
              <div role="gridcell" className={`luma-month-day${day.inMonth ? "" : " is-muted"}`} key={day.date} data-date={day.date}
                aria-label={`${day.date}${day.items.length ? `: ${day.items.map(item => item.label).join(", ")}` : ""}`}>
                <span className="luma-month-number">{Number(day.date.slice(-2))}</span>
                <div className="luma-month-pins">
                  {day.items.map((item, index) => (
                    <span className={`luma-month-pin mark-${item.type}${item.isPrediction ? " is-estimate" : ""}`} data-event={item.type} key={`${item.type}-${index}`} title={item.label}>
                      <i aria-hidden="true" /><span>{item.label}</span>
                    </span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        ))}
      </div>
      <div className="luma-month-legend" aria-label="Legenda do calendário">
        {entries.map(item => <span className={`mark-${item.type}`} key={item.type} data-legend={item.type}><i aria-hidden="true" />{item.label}</span>)}
      </div>
    </div>
  );
}
