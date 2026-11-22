"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowLeft, ChevronLeft, ChevronRight, Loader2 } from "lucide-react";
import { getAccountCalendar, getAccountProfile, type AccountProfile, type CycleCalendar } from "@/lib/luma-api";
import { CycleCalendarGrid } from "@/components/molecules/CycleCalendarGrid";

type ProfileCalendarPageTemplateProps = {
  accountId: string;
};

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
            <button className="account-secondary calendar-nav" type="button" title="Mês anterior" aria-label="Mês anterior" onClick={() => moveMonth(-1)}>
              <ChevronLeft size={18} />
            </button>
            <strong>{formatMonthTitle(month)}</strong>
            <button className="account-secondary calendar-nav" type="button" title="Próximo mês" aria-label="Próximo mês" onClick={() => moveMonth(1)}>
              <ChevronRight size={18} />
            </button>
          </div>

          {loading ? (
            <div className="calendar-loading"><Loader2 size={20} /> Carregando calendário...</div>
          ) : calendar ? (
            <CycleCalendarGrid calendar={calendar} label={`Calendário de ${formatMonthTitle(month)}`} />
          ) : null}

          {status && <p className="account-status info">{status}</p>}

          <div className="calendar-summary">
            <span>Última menstruação: {formatDateOnly(calendar?.summary.lastPeriodDate)}</span>
            <span>Próxima previsão: {calendar?.summary.activePregnancy ? "pausada por gravidez ativa" : formatDateOnly(calendar?.summary.nextPeriodDate)}</span>
            <span>DPP: {formatDateOnly(calendar?.summary.estimatedDueDate)}</span>
          </div>

        </section>
      </section>
    </main>
  );
}

function toYearMonth(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
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
