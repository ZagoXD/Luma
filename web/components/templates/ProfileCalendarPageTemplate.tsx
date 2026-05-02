"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowLeft, ChevronLeft, ChevronRight, Loader2 } from "lucide-react";
import { getAccountCalendar, getAccountProfile, type AccountProfile, type CycleCalendar } from "@/lib/luma-api";

type ProfileCalendarPageTemplateProps = {
  accountId: string;
};

const weekDays = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

const legend = [
  { type: "period_start_recorded", label: "Início registrado" },
  { type: "period_day_recorded", label: "Menstruação registrada" },
  { type: "period_start_predicted", label: "Previsão menstrual" },
  { type: "fertile_window_estimated", label: "Janela fértil" },
  { type: "ovulation_estimated", label: "Ovulação" },
  { type: "sexual_activity_recorded", label: "Relação sexual" },
  { type: "pregnancy_week", label: "Gravidez" },
  { type: "estimated_due_date", label: "Semana prevista para parto" },
];

export function ProfileCalendarPageTemplate({ accountId }: ProfileCalendarPageTemplateProps) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const initialMonth = searchParams.get("month") || toYearMonth(new Date());
  const [month, setMonth] = useState(initialMonth);
  const [profile, setProfile] = useState<AccountProfile | null>(null);
  const [calendar, setCalendar] = useState<CycleCalendar | null>(null);
  const [status, setStatus] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;

    async function loadProfile() {
      try {
        const nextProfile = await getAccountProfile();
        if (!active) return;

        if (nextProfile.user.id !== accountId) {
          router.replace(`/perfil/${nextProfile.user.id}/calendario?month=${month}`);
          return;
        }

        setProfile(nextProfile);
      } catch (error) {
        setStatus(error instanceof Error ? error.message : "Não consegui carregar seu perfil.");
        router.replace(`/login?redirect=${encodeURIComponent(`/perfil/${accountId}/calendario?month=${month}`)}`);
      }
    }

    loadProfile();
    return () => {
      active = false;
    };
  }, [accountId, month, router]);

  useEffect(() => {
    let active = true;

    async function loadCalendar() {
      try {
        const nextCalendar = await getAccountCalendar(month);
        if (!active) return;
        setCalendar(nextCalendar);
      } catch (error) {
        if (!active) return;
        setCalendar(null);
        setStatus(error instanceof Error ? error.message : "Não consegui carregar o calendário agora.");
      } finally {
        if (active) setLoading(false);
      }
    }

    loadCalendar();
    return () => {
      active = false;
    };
  }, [month]);

  const visibleDays = useMemo(() => buildVisibleDays(calendar), [calendar]);

  function moveMonth(delta: number) {
    const next = addMonths(month, delta);
    setLoading(true);
    setStatus("");
    setMonth(next);
    router.replace(`/perfil/${accountId}/calendario?month=${next}`);
  }

  return (
    <main className="account-page">
      <section className="account-shell">
        <div className="profile-topbar">
          <Link href={`/perfil/${accountId}`} className="account-back">
            <ArrowLeft size={18} />
            Voltar ao perfil
          </Link>
        </div>

        <div className="account-heading profile-heading">
          <span className="account-kicker">Calendário da Luma</span>
          <h1>{formatMonthTitle(month)}</h1>
          <p>{profile?.user.fullName ? `Registros e previsões de ${profile.user.fullName}.` : "Registros e previsões do seu ciclo."}</p>
        </div>

        <section className="account-panel calendar-panel">
          <div className="calendar-toolbar">
            <button className="account-secondary calendar-nav" type="button" onClick={() => moveMonth(-1)}>
              <ChevronLeft size={18} />
              Mês anterior
            </button>
            <strong>{formatMonthTitle(month)}</strong>
            <button className="account-secondary calendar-nav" type="button" onClick={() => moveMonth(1)}>
              Próximo mês
              <ChevronRight size={18} />
            </button>
          </div>

          {loading ? (
            <div className="calendar-loading"><Loader2 size={20} /> Carregando calendário...</div>
          ) : (
            <div className="calendar-grid" role="grid" aria-label={`Calendário de ${formatMonthTitle(month)}`}>
              {weekDays.map((day) => (
                <div className="calendar-weekday" key={day}>{day}</div>
              ))}
              {visibleDays.map((day) => (
                <div className={`calendar-day ${day.inMonth ? "" : "muted"}`} key={day.date}>
                  <span>{Number(day.date.slice(-2))}</span>
                  <div className="calendar-pins">
                    {day.items.map((item) => (
                      <span className={`calendar-pin pin-${item.type}`} key={item.type} title={item.label}>
                        {shortLabel(item.label)}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}

          {status && <p className="account-status info">{status}</p>}

          <div className="calendar-summary">
            <span>Última menstruação: {formatDateOnly(calendar?.summary.lastPeriodDate)}</span>
            <span>Próxima previsão: {calendar?.summary.activePregnancy ? "pausada por gravidez ativa" : formatDateOnly(calendar?.summary.nextPeriodDate)}</span>
            <span>DPP: {formatDateOnly(calendar?.summary.estimatedDueDate)}</span>
          </div>

          <div className="calendar-legend">
            {legend.map((item) => (
              <span key={item.type}><i className={`legend-dot pin-${item.type}`} />{item.label}</span>
            ))}
          </div>
        </section>
      </section>
    </main>
  );
}

function buildVisibleDays(calendar: CycleCalendar | null) {
  if (!calendar) return [];
  const [year, month] = calendar.month.split("-").map(Number);
  const first = new Date(Date.UTC(year, month - 1, 1));
  const offset = first.getUTCDay();
  const byDate = new Map(calendar.days.map((day) => [day.date, day]));
  const start = new Date(Date.UTC(year, month - 1, 1 - offset));

  return Array.from({ length: 42 }, (_, index) => {
    const date = new Date(start);
    date.setUTCDate(start.getUTCDate() + index);
    const key = toYearMonthDay(date);
    return {
      date: key,
      inMonth: key.startsWith(calendar.month),
      items: byDate.get(key)?.items || [],
    };
  });
}

function toYearMonth(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}

function toYearMonthDay(date: Date) {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}-${String(date.getUTCDate()).padStart(2, "0")}`;
}

function addMonths(month: string, delta: number) {
  const [year, monthNumber] = month.split("-").map(Number);
  const date = new Date(Date.UTC(year, monthNumber - 1 + delta, 1));
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
}

function formatMonthTitle(month: string) {
  const [year, monthNumber] = month.split("-").map(Number);
  return new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(Date.UTC(year, monthNumber - 1, 1)));
}

function formatDateOnly(value?: string | null) {
  if (!value) return "não informado";
  return new Intl.DateTimeFormat("pt-BR", { timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}

function shortLabel(label: string) {
  return label.length > 18 ? `${label.slice(0, 16)}...` : label;
}
