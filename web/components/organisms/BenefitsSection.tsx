import { BenefitCard } from "@/components/molecules/Cards";
import { SectionHeading } from "@/components/molecules/SectionHeading";
import { MessageCircle, Zap, BarChart2, Target, BellRing, MessagesSquare, ShieldCheck, Moon } from "lucide-react";

const iconProps = { size: 20, strokeWidth: 1.75 };

const benefits = [
  [<MessageCircle {...iconProps} />, "Sem abrir novo app", "Tudo via mensagem, no canal que você já usa todos os dias."],
  [<Zap {...iconProps} />, "Registro em segundos", "Uma mensagem curta já é suficiente. Sem campos, sem navegação."],
  [<BarChart2 {...iconProps} />, "Histórico organizado", "Seus ciclos ficam registrados e acessíveis quando você quiser."],
  [<Target {...iconProps} />, "Previsões aproximadas", "A Luma estima a próxima menstruação com base no seu histórico."],
  [<BellRing {...iconProps} />, "Lembretes opcionais", "Ative só o que faz sentido para a sua rotina."],
  [<MessagesSquare {...iconProps} />, "Linguagem humana", "Nada de termos médicos ou menus confusos. Só conversa."],
  [<ShieldCheck {...iconProps} />, "Privacidade como prioridade", "Seus dados são seus. Pensado com consentimento desde o início."],
  [<Moon {...iconProps} />, "Simples e sem julgamentos", "Um espaço só seu, discreto e sem complicação."],
];

export function BenefitsSection() {
  return (
    <section className="beneficios">
      <div className="section-inner">
        <SectionHeading
          tag="Por que a Luma"
          title={
            <>
              Feita para a
              <br />
              <em>rotina real</em>
            </>
          }
          centered
        />
        <div className="benefits-grid">
          {benefits.map(([icon, title, text], index) => (
            <BenefitCard
              key={title}
              delay={`${0.06 * (index + 1)}s`}
              icon={icon}
              title={title}
              text={text}
              reverse={index % 2 === 1}
            />
          ))}
        </div>
      </div>
    </section>
  );
}
