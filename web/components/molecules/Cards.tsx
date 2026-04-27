import type { ReactNode } from "react";

type CardProps = {
  className: string;
  delay?: string;
  icon?: ReactNode;
  title: string;
  text: ReactNode;
};

export function InfoCard({ className, delay, icon, title, text }: CardProps) {
  return (
    <div className={`${className} fade-up`} style={delay ? { transitionDelay: delay } : undefined}>
      {icon}
      <h4>{title}</h4>
      <p>{text}</p>
    </div>
  );
}

type SimpleCardProps = {
  delay?: string;
  icon: ReactNode;
  title: string;
  text: ReactNode;
};

export function FeatureCard({ delay, icon, title, text }: SimpleCardProps) {
  return (
    <div className="feat-card fade-up" style={delay ? { transitionDelay: delay } : undefined}>
      <div className="feat-icon-wrap">{icon}</div>
      <h4>{title}</h4>
      <p>{text}</p>
    </div>
  );
}

export function BenefitCard({ delay, icon, title, text, reverse: _reverse }: SimpleCardProps & { reverse?: boolean }) {
  return (
    <div
      className="benefit fade-up"
      style={delay ? { transitionDelay: delay } : undefined}
    >
      <div className="benefit-icon">{icon}</div>
      <h4>{title}</h4>
      <p>{text}</p>
    </div>
  );
}

export function PainCard({ delay, icon, title, text }: Omit<CardProps, "className">) {
  return (
    <div className="pain-card fade-up" style={delay ? { transitionDelay: delay } : undefined}>
      <span className="pain-icon">{icon}</span>
      <h3>{title}</h3>
      <p>{text}</p>
    </div>
  );
}
