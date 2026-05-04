import { CalendarDays } from "lucide-react";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const weekdays = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

const demoPins: Record<number, Array<"period-start" | "period" | "period-end" | "fertile" | "ovulation" | "sex">> = {
  3: ["period-start"],
  4: ["period"],
  5: ["period"],
  6: ["period-end"],
  13: ["fertile"],
  14: ["fertile"],
  15: ["ovulation"],
  16: ["fertile"],
  17: ["fertile"],
  22: ["sex"],
  31: ["period-start"],
};

const legend = [
  ["period-start", "Início"],
  ["period", "Menstruação"],
  ["period-end", "Fim"],
  ["fertile", "Período fértil"],
  ["ovulation", "Ovulação"],
  ["sex", "Relação"],
] as const;

export function CalendarPreviewSection() {
  return (
    <section className="calendar-preview-section" id="calendario">
      <div className="section-inner calendar-preview-grid">
        <div className="fade-up">
          <SectionHeading
            tag="Calendário visual"
            title={
              <>
                Seus registros viram um mapa simples do <em>ciclo</em>.
              </>
            }
            lead="Além da conversa no WhatsApp, o painel mostra um calendário mensal com registros e estimativas para você enxergar padrões com mais clareza."
          />
        </div>

        <div className="calendar-preview-card fade-up" style={{ transitionDelay: "0.15s" }}>
          <div className="calendar-preview-top">
            <span>
              <CalendarDays size={18} />
              Maio de 2026
            </span>
            <strong>Demonstração</strong>
          </div>

          <div className="calendar-preview-grid-days" aria-label="Demonstração de calendário menstrual">
            {weekdays.map((day) => (
              <div className="calendar-preview-weekday" key={day}>
                {day}
              </div>
            ))}
            {Array.from({ length: 35 }, (_, index) => {
              const day = index - 4;
              const inMonth = day >= 1 && day <= 31;
              const pins = inMonth ? demoPins[day] ?? [] : [];

              return (
                <div className={`calendar-preview-day ${inMonth ? "" : "muted"}`} key={index}>
                  <span>{inMonth ? day : ""}</span>
                  <div className="calendar-preview-pins">
                    {pins.map((pin) => (
                      <i className={`calendar-preview-pin pin-${pin}`} key={pin} />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>

          <div className="calendar-preview-legend">
            {legend.map(([type, label]) => (
              <span key={type}>
                <i className={`calendar-preview-pin pin-${type}`} />
                {label}
              </span>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
