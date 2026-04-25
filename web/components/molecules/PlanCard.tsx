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
}: PlanCardProps) {
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
            <span className={`check ${feature.enabled ? "on" : "off"}`}>{feature.enabled ? "✓" : "–"}</span>{" "}
            {feature.label}
          </li>
        ))}
      </ul>
      <button className={`plan-cta ${ctaClass}`} data-scroll-target="lista">
        {cta}
      </button>
      <p className="plan-note">{note}</p>
    </div>
  );
}
