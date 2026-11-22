"use client";
import { useState } from "react";
import Link from "next/link";
import { Send } from "lucide-react";
import { formatBrazilPhone } from "@/lib/account-format";
import { validateWaitlist, waitlistConsentVersion, waitlistLegalVersion } from "@/lib/waitlist";
export function WaitlistForm() {
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [channel, setChannel] = useState("email");
  const [phone, setPhone] = useState("");
  return <section className="cta-final waitlist-section" id="lista"><div className="section-inner">
    <p className="section-tag">Lista de espera</p><h2 className="section-h2">Conheça a Luma em primeira mão.</h2>
    <p>Deixe seu contato para receber o aviso de lançamento. Sem cobrança e sem criar uma conta.</p>
    {!done && <form className="waitlist-form" onSubmit={async event => {
      event.preventDefault(); const form = new FormData(event.currentTarget); setMessage("");
      const input = validateWaitlist({ name: form.get("name"), email: form.get("email"), whatsapp: phone, channel,
        termsAccepted: form.get("termsAccepted") === "on", launchConsent: form.get("launchConsent") === "on",
        version: waitlistConsentVersion, termsVersion: waitlistLegalVersion, privacyVersion: waitlistLegalVersion });
      if (!input) { setMessage("Confira os contatos dos canais escolhidos e marque separadamente os dois aceites."); return; }
      setBusy(true);
      try {
        const response = await fetch("/api/waitlist", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ...input, website: form.get("website") }) });
        const data = await response.json(); setMessage(data.message); if (response.ok) setDone(true);
      } catch { setMessage("Não foi possível enviar agora. Tente novamente mais tarde."); } finally { setBusy(false); }
    }}>
      <label>Como podemos chamar você?<input name="name" autoComplete="given-name" minLength={2} maxLength={100} required /></label>
      <fieldset className="waitlist-channels"><legend>Onde você quer receber o aviso?</legend>
        <div>{[["email", "E-mail"], ["whatsapp", "WhatsApp"], ["both", "Ambos"]].map(([value, label]) =>
          <label key={value}><input name="channel" type="radio" value={value} checked={channel === value} onChange={() => setChannel(value)} /><span>{label}</span></label>)}</div>
      </fieldset>
      {channel !== "whatsapp" && <label>E-mail<input name="email" type="email" autoComplete="email" maxLength={254} required /></label>}
      {channel !== "email" && <label>WhatsApp com DDD<input name="whatsapp" type="tel" inputMode="tel" autoComplete="tel-national" value={phone}
        placeholder="(16) 99233-0309" maxLength={20} onChange={event => setPhone(formatBrazilPhone(event.target.value))} required />
        <small className="waitlist-hint">Número brasileiro com DDD e nove dígitos, incluindo o 9.</small></label>}
      <div className="waitlist-trap" aria-hidden="true"><label>Website<input name="website" tabIndex={-1} autoComplete="off" /></label></div>
      <label className="account-consent"><input name="termsAccepted" type="checkbox" required /><span>Li e aceito os <Link href="/termos" target="_blank" rel="noopener noreferrer">Termos de Uso</Link> e declaro que li a <Link href="/privacidade" target="_blank" rel="noopener noreferrer">Política de Privacidade</Link>.</span></label>
      <label className="account-consent"><input name="launchConsent" type="checkbox" required /><span>Autorizo a Luma a usar os contatos informados para enviar o aviso de lançamento pelos canais que escolhi. Posso retirar esta autorização e pedir minha remoção a qualquer momento.</span></label>
      <button type="submit" className="account-primary" disabled={busy}><Send size={16} />{busy ? "Enviando..." : "Quero entrar na lista"}</button>
    </form>}
    {message && <p role="status">{message}</p>}
  </div></section>;
}
