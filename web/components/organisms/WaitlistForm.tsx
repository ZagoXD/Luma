"use client";
import { useState } from "react";
import Link from "next/link";
import { Send } from "lucide-react";
export function WaitlistForm() {
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  return <section className="cta-final waitlist-section" id="lista"><div className="section-inner">
    <p className="section-tag">Lista de espera</p><h2 className="section-h2">Conheça a Luma em primeira mão.</h2>
    <p>Deixe seu contato para receber o aviso de lançamento. Sem cobrança e sem criar uma conta.</p>
    {!done && <form className="waitlist-form" onSubmit={async event => {
      event.preventDefault(); const form = new FormData(event.currentTarget); setBusy(true); setMessage("");
      try {
        const response = await fetch("/api/waitlist", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: form.get("name"), email: form.get("email"), consent: form.get("consent") === "on", website: form.get("website") }) });
        const data = await response.json(); setMessage(data.message); if (response.ok) setDone(true);
      } catch { setMessage("Não foi possível enviar agora. Tente novamente mais tarde."); } finally { setBusy(false); }
    }}>
      <label>Como podemos chamar você?<input name="name" autoComplete="given-name" minLength={2} maxLength={100} required /></label>
      <label>E-mail<input name="email" type="email" autoComplete="email" maxLength={254} required /></label>
      <div className="waitlist-trap" aria-hidden="true"><label>Website<input name="website" tabIndex={-1} autoComplete="off" /></label></div>
      <label className="account-consent"><input name="consent" type="checkbox" required /><span>Quero receber o aviso de lançamento por e-mail. Li a <Link href="/privacidade">Política de Privacidade</Link> e posso pedir minha remoção a qualquer momento.</span></label>
      <button type="submit" className="account-primary" disabled={busy}><Send size={16} />{busy ? "Enviando..." : "Quero entrar na lista"}</button>
    </form>}
    {message && <p role="status">{message}</p>}
  </div></section>;
}
