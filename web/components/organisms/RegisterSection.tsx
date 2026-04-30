import { FeatureCard } from "@/components/molecules/Cards";
import { SectionHeading } from "@/components/molecules/SectionHeading";
import { CircleDot, CircleCheck, Droplets, HeartPulse, Smile, Bell, CalendarDays, Lock } from "lucide-react";
import type { ReactNode } from "react";

const iconProps = { size: 20, strokeWidth: 1.75 };

const items: [ReactNode, string, string][] = [
  [<CircleDot key="start" {...iconProps} />, "Início da menstruação", 'Só falar "menstruei hoje" — ela registra e inicia o ciclo.'],
  [<CircleCheck key="end" {...iconProps} />, "Fim da menstruação", '"Acabou ontem" já é suficiente para fechar o ciclo.'],
  [<Droplets key="flow" {...iconProps} />, "Intensidade do fluxo", "Leve, médio ou intenso — você atualiza quando quiser."],
  [<HeartPulse key="symptoms" {...iconProps} />, "Cólicas e sintomas", "Dor de cabeça, náusea, cansaço, inchaço e muito mais."],
  [<Smile key="mood" {...iconProps} />, "Humor e bem-estar", "Registre como você está se sentindo ao longo do ciclo."],
  [<Bell key="reminder" {...iconProps} />, "Lembretes", "Aviso antes da próxima menstruação ou para anticoncepcional."],
  [<CalendarDays key="history" {...iconProps} />, "Histórico do ciclo", "Consulte duração média, sintomas frequentes e padrões."],
  [<Lock key="private" {...iconProps} />, "Registros íntimos", "Dados opcionais e privados, armazenados com cuidado."],
];

export function RegisterSection() {
  return (
    <section className="registrar">
      <div className="section-inner">
        <SectionHeading
          tag="Funcionalidades"
          title={
            <>
              O que você poderá
              <br />
              <em>registrar e consultar</em>
            </>
          }
          lead="Tudo em linguagem simples, sem formulários complicados."
          centered
        />
        <div className="register-grid">
          {items.map(([icon, title, text], index) => (
            <FeatureCard
              key={title}
              delay={`${0.06 * (index + 1)}s`}
              icon={icon}
              title={title}
              text={text}
            />
          ))}
        </div>
      </div>
    </section>
  );
}
