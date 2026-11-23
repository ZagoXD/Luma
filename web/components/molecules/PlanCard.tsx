"use client";

import { Check, Minus } from "lucide-react";
import { useRouter } from "next/navigation";
import { isWaitlist, purchasePath, waitlistPlanEvent } from "@/lib/site-mode";

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
  secondaryCta?: string;
  secondaryNote?: string;
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
  secondaryCta,
  secondaryNote,
  featured,
  delay,
  planValue,
}: PlanCardProps) {
  const router = useRouter();

  return (
    <div className={`plan-card ${featured ? "featured " : ""}fade-up`} style={{ transitionDelay: delay }}>
      <span className={`plan-badge ${badgeClass}`}>{badge}</span>
      <div className="plan-name">{name}</div>
      {!isWaitlist && <div className="plan-price">{price}</div>}
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
      <button className={`plan-cta ${ctaClass}`} onClick={() => {
        if (isWaitlist) window.dispatchEvent(new CustomEvent(waitlistPlanEvent, { detail: planValue }));
        router.push(purchasePath(planValue, "annual"));
      }}>
        {isWaitlist ? "Quero esse plano no lançamento" : cta}
      </button>
      {secondaryCta && !isWaitlist && (
        <button className="plan-cta secondary-plan-cta" onClick={() => router.push(`/checkout/${planValue}?billing=monthly`)}>
          {secondaryCta}
        </button>
      )}
      <p className="plan-note">{isWaitlist ? "Entrar na lista indica interesse, sem contratar o plano." : note}</p>
      {secondaryNote && !isWaitlist && <p className="plan-note secondary-note">{secondaryNote}</p>}
    </div>
  );
}
