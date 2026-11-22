import { CalendarDays } from "lucide-react";
import { SectionHeading } from "@/components/molecules/SectionHeading";
import { CycleCalendarGrid } from "@/components/molecules/CycleCalendarGrid";
import { calendarDemo } from "@/lib/calendar-view";

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
              Novembro de 2026
            </span>
            <strong>Demonstração</strong>
          </div>

          <CycleCalendarGrid calendar={calendarDemo} legend="visible" label="Demonstração de calendário menstrual" />
        </div>
      </div>
    </section>
  );
}
