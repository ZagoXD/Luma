"use client";

import { FormEvent, useState } from "react";
import { supabase } from "@/lib/supabase";

function normalizeWhatsApp(value: string) {
  const digits = value.replace(/\D/g, "");
  if (!digits) return "";
  if (digits.startsWith("55")) return digits;
  if (digits.length === 10 || digits.length === 11) return `55${digits}`;
  return digits;
}

function isValidWhatsApp(value: string) {
  const normalized = normalizeWhatsApp(value);
  return normalized.length >= 12 && normalized.length <= 13;
}

export async function refreshWaitlistCount() {
  const counterEl = document.getElementById("counter-num");
  if (!counterEl) return;

  try {
    const { data, error } = await supabase.rpc("get_waitlist_count");
    if (error) throw error;
    animateCounterTo(counterEl, data ?? 0);
  } catch (error) {
    console.error("Erro ao buscar contador da waitlist:", error);
    counterEl.textContent = "-";
  }
}

function animateCounterTo(counterEl: HTMLElement, targetValue: number) {
  const safeTarget = Math.max(0, Number(targetValue) || 0);
  const startValue = Number(counterEl.textContent?.replace(/\D/g, "")) || 0;
  const duration = 900;
  const startTime = performance.now();

  function frame(now: number) {
    const progress = Math.min((now - startTime) / duration, 1);
    const eased = 1 - Math.pow(1 - progress, 3);
    const value = Math.round(startValue + (safeTarget - startValue) * eased);
    counterEl.textContent = value.toLocaleString("pt-BR");
    if (progress < 1) requestAnimationFrame(frame);
  }

  requestAnimationFrame(frame);
}

export function WaitlistSection() {
  const [errors, setErrors] = useState<Record<string, boolean>>({});
  const [status, setStatus] = useState<{ message: string; type: "info" | "error" }>({ message: "", type: "info" });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [success, setSuccess] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const nome = String(form.get("nome") || "").trim();
    const contato = String(form.get("contato") || "").trim();
    const whatsappNormalizado = normalizeWhatsApp(contato);
    const plano = String(form.get("plano") || "");
    const dificuldade = String(form.get("dificuldade") || "").trim();
    const consent = form.get("consent") === "on";

    const nextErrors = {
      nome: !nome,
      contato: !isValidWhatsApp(contato),
      plano: !plano,
      consent: !consent,
    };
    setErrors(nextErrors);
    setStatus({ message: "", type: "info" });

    if (Object.values(nextErrors).some(Boolean)) return;

    setIsSubmitting(true);
    setStatus({ message: "Enviando seu cadastro para a lista de espera…", type: "info" });

    try {
      const { data: alreadyRegistered, error: duplicateCheckError } = await supabase.rpc(
        "is_waitlist_whatsapp_registered",
        { input_whatsapp: whatsappNormalizado },
      );

      if (duplicateCheckError) throw duplicateCheckError;

      if (alreadyRegistered) {
        setStatus({ message: "Esse WhatsApp já está cadastrado na lista de espera.", type: "error" });
        setIsSubmitting(false);
        return;
      }

      const { error } = await supabase.from("waitlist_signups").insert({
        name: nome,
        whatsapp: contato,
        whatsapp_normalized: whatsappNormalizado,
        desired_plan: plano,
        challenge: dificuldade || null,
        consent_accepted: consent,
        source: "landing-page",
        page_path: window.location.pathname,
      });

      if (error) throw error;

      setSuccess(true);
      setStatus({ message: "", type: "info" });
      refreshWaitlistCount();
    } catch (error) {
      console.error("Erro ao salvar lead no Supabase:", error);
      const maybeError = error as { code?: string; message?: string };
      const isDuplicate =
        maybeError?.code === "23505" || String(maybeError?.message || "").toLowerCase().includes("duplicate");
      setStatus({
        message: isDuplicate
          ? "Esse WhatsApp já está cadastrado na lista de espera."
          : "Não consegui enviar agora. Verifique a configuração do Supabase e tente novamente.",
        type: "error",
      });
      setIsSubmitting(false);
    }
  }

  return (
    <section className="cta-final" id="lista">
      <div className="section-inner">
        <div className="fade-up">
          <p className="section-tag">Lista de espera</p>
          <h2 className="section-h2" style={{ color: "#fff" }}>
            Seja das primeiras
            <br />a testar a <em style={{ color: "var(--lavender)" }}>Luma.</em>
          </h2>
          <p className="section-lead">
            Cadastre seu interesse. Quando a primeira versão estiver pronta, você recebe antes de todo mundo.
          </p>
        </div>
        <div className="form-wrapper fade-up" style={{ transitionDelay: "0.1s" }}>
          <form className={`form-real ${success ? "hide" : ""}`} id="form-real" onSubmit={handleSubmit}>
            <div className="form-grid">
              <div className="form-field">
                <label htmlFor="f-nome">Seu nome *</label>
                <input type="text" id="f-nome" name="nome" placeholder="Como posso te chamar?" autoComplete="given-name" />
                <span className={`field-error ${errors.nome ? "show" : ""}`}>Por favor, informe seu nome.</span>
              </div>
              <div className="form-field">
                <label htmlFor="f-contato">WhatsApp *</label>
                <input
                  type="text"
                  id="f-contato"
                  name="contato"
                  placeholder="(11) 99999-9999"
                  inputMode="tel"
                  autoComplete="tel"
                />
                <span className={`field-error ${errors.contato ? "show" : ""}`}>
                  Por favor, informe um WhatsApp válido.
                </span>
              </div>
              <div className="form-field full">
                <label>Qual plano você escolheria? *</label>
                <div className="plan-selector" role="radiogroup" aria-label="Plano de interesse">
                  {[
                    ["lista-espera", "Lista de espera", "Quero acompanhar primeiro e conhecer a Luma quando lançar."],
                    ["basico", "Básico", "Registro simples do ciclo por mensagem por R$ 5,90/mês."],
                    ["essencial", "Essencial", "Inclui lembretes inteligentes por R$ 9,90/mês."],
                  ].map(([value, title, text]) => (
                    <label className="plan-option" key={value}>
                      <input type="radio" name="plano" value={value} />
                      <span className="plan-option-card">
                        <strong>{title}</strong>
                        <span>{text}</span>
                      </span>
                    </label>
                  ))}
                </div>
                <span className={`field-error plan-error ${errors.plano ? "show" : ""}`}>
                  Escolha o plano que mais combina com você.
                </span>
              </div>
              <div className="form-field full">
                <label htmlFor="f-dificuldade">
                  Qual sua maior dificuldade com apps de ciclo?{" "}
                  <em style={{ color: "var(--text-light)", fontStyle: "normal", fontSize: "0.75rem" }}>(opcional)</em>
                </label>
                <textarea
                  id="f-dificuldade"
                  name="dificuldade"
                  placeholder="Escreva à vontade — sua resposta vai ajudar muito..."
                />
              </div>
            </div>
            <div className="form-check">
              <input type="checkbox" id="f-consent" name="consent" />
              <label htmlFor="f-consent">
                Concordo em ser contactada pela equipe da Luma quando o produto for lançado. Entendo que meus dados
                serão usados apenas para esse fim e que posso pedir exclusão a qualquer momento.
              </label>
            </div>
            <span className={`field-error ${errors.consent ? "show" : ""}`} style={{ marginBottom: "0.75rem" }}>
              Você precisa aceitar para continuar.
            </span>
            <button className="btn-submit" id="btn-submit" disabled={isSubmitting}>
              {isSubmitting ? "Enviando…" : "✦  Quero entrar na lista de espera"}
            </button>
            <div className={`form-status ${status.message ? `show ${status.type}` : ""}`} aria-live="polite">
              {status.message}
            </div>
          </form>

          <div className={`form-success ${success ? "show" : ""}`} id="form-success">
            <div className="success-icon">🌙</div>
            <h3>Você está na lista!</h3>
            <p>
              Obrigada por se interessar pela Luma. Quando a primeira versão estiver pronta, você será das primeiras a
              saber. 💛
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
