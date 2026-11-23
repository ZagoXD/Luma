"use client";

import { useEffect, useState } from "react";
import { Activity, RefreshCcw } from "lucide-react";

type Usage = {
  dailyLimit: number; dailyUsed: number; monthlyAudioSeconds: number;
  audioSecondsUsed: number; monthlyImages: number; imagesUsed: number; timeZone: string;
  monthlyLimit: number; monthlyUsed: number; dailyResetsAt: string; monthlyResetsAt: string;
};

export function AccountUsage() {
  const [usage, setUsage] = useState<Usage>();
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => { if (document.visibilityState === "visible") setRefresh(value => value + 1); }, 60_000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/account/usage", { cache: "no-store", signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw new Error("Não foi possível consultar seu uso agora.");
        return response.json() as Promise<Usage>;
      }).then(data => { setUsage(data); setError(""); })
      .catch((reason: Error) => { if (!controller.signal.aborted) setError(reason.message); });
    return () => controller.abort();
  }, [refresh]);
  return <section className="account-panel profile-card">
    <h2><Activity size={20} /> Uso do plano</h2>
    {error ? <p role="status">{error}</p> : usage ? <>
      <div className="usage-meters">
        <UsageMeter id="daily-ai" label="Mensagens com IA hoje" used={usage.dailyUsed} limit={usage.dailyLimit} reset={usage.dailyResetsAt} zone={usage.timeZone} />
        <UsageMeter id="monthly-ai" label="Mensagens com IA neste mês" used={usage.monthlyUsed} limit={usage.monthlyLimit} reset={usage.monthlyResetsAt} zone={usage.timeZone} />
        {usage.monthlyAudioSeconds > 0 && <UsageMeter id="monthly-audio" label="Áudio neste mês (segundos)" used={usage.audioSecondsUsed} limit={usage.monthlyAudioSeconds} reset={usage.monthlyResetsAt} zone={usage.timeZone} />}
        {usage.monthlyImages > 0 && <UsageMeter id="monthly-images" label="Imagens neste mês" used={usage.imagesUsed} limit={usage.monthlyImages} reset={usage.monthlyResetsAt} zone={usage.timeZone} />}
      </div>
      <p>Fuso das renovações: {usage.timeZone}. As cotas diária e mensal são simultâneas. Áudios usam uma mensagem de cada cota e sua duração no saldo de áudio.</p>
      <p>Seus registros pelo menu do WhatsApp, calendário e configurações continuam disponíveis após a cota de IA.</p>
    </> : <p role="status">Consultando uso...</p>}
    <button type="button" className="account-secondary billing-button" onClick={() => setRefresh(value => value + 1)}><RefreshCcw size={16} />Atualizar uso</button>
  </section>;
}

function UsageMeter({ id, label, used, limit, reset, zone }: {
  id: string; label: string; used: number; limit: number; reset: string; zone: string;
}) {
  const resetLabel = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short", timeZone: zone }).format(new Date(reset));
  return <div className="usage-meter">
    <div><label htmlFor={id}>{label}</label><strong>{used} de {limit}</strong></div>
    <progress id={id} max={Math.max(1, limit)} value={Math.max(0, Math.min(used, limit))} />
    <small>Renova em {resetLabel}</small>
  </div>;
}
