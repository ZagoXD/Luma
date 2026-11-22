"use client";

import { useEffect, useState } from "react";
import { Activity, RefreshCcw } from "lucide-react";

type Usage = {
  dailyLimit: number; dailyUsed: number; monthlyAudioSeconds: number;
  audioSecondsUsed: number; monthlyImages: number; imagesUsed: number; timeZone: string;
};

export function AccountUsage() {
  const [usage, setUsage] = useState<Usage>();
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
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
      <dl className="profile-list">
        <div><dt>Mensagens com IA hoje</dt><dd>{usage.dailyUsed} de {usage.dailyLimit}</dd></div>
        {usage.monthlyAudioSeconds > 0 && <div><dt>Áudio neste mês</dt><dd>{Math.floor(usage.audioSecondsUsed / 60)}min {usage.audioSecondsUsed % 60}s de {usage.monthlyAudioSeconds / 60}min</dd></div>}
        {usage.monthlyImages > 0 && <div><dt>Imagens neste mês</dt><dd>{usage.imagesUsed} de {usage.monthlyImages}</dd></div>}
      </dl>
      <p>A cota de IA renova diariamente à meia-noite ({usage.timeZone}). Não há limite mensal de conversas. Áudios usam uma conversa e sua duração no saldo de áudio.</p>
      <p>Seus registros pelo menu do WhatsApp, calendário e configurações continuam disponíveis após a cota de IA.</p>
    </> : <p role="status">Consultando uso...</p>}
    <button type="button" className="account-secondary billing-button" onClick={() => setRefresh(value => value + 1)}><RefreshCcw size={16} />Atualizar uso</button>
  </section>;
}
