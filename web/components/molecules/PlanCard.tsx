"use client";

import { Check, Minus } from "lucide-react";

type PlanCardProps = {
  badge: string;
  badgeClass: string;
  name: string;
  price: React.ReactNode;
  description: string;
  features: Array<{ enabled: boolean; label: string }>;
  cta: string;
  ctaClass: string;
  note: string;
  featured?: boolean;
  delay: string;
  planValue: string;
};

export function PlanCard({
  badge,
  badgeClass,
  name,
  price,
  description,
  features,
  cta,
  ctaClass,
  note,
  featured,
  delay,
  planValue,
}: PlanCardProps) {
  function handleSelectPlan() {
    // Seleciona o radio do plano no formulário
    const radio = document.querySelector<HTMLInputElement>(
      `input[name="plano"][value="${planValue}"]`
    );
    if (radio) {
      radio.checked = true;
      radio.dispatchEvent(new Event("change", { bubbles: true }));
    }
    // Scroll suave até o formulário
    const target = document.getElementById("lista");
    if (target) {
      target.scrollIntoView({ behavior: "smooth" });
    }
  }

  return (
    <div className={`plan-card ${featured ? "featured " : ""}fade-up`} style={{ transitionDelay: delay }}>
      <span className={`plan-badge ${badgeClass}`}>{badge}</span>
      <div className="plan-name">{name}</div>
      <div className="plan-price">{price}</div>
      <p className="plan-desc">{description}</p>
      <div className="plan-divider" />
      <ul className="plan-features">
        {features.map((feature) => (
          <li key={feature.label}>
            <span className={`check ${feature.enabled ? "on" : "off"}`}>
              {feature.enabled ? <Check size={10} strokeWidth={3} /> : <Minus size={10} strokeWidth={3} />}
            </span>{" "}
            {feature.label}
          </li>
        ))}
      </ul>
      <button className={`plan-cta ${ctaClass}`} onClick={handleSelectPlan}>
        {cta}
      </button>
      <p className="plan-note">{note}</p>
    </div>
  );
}
