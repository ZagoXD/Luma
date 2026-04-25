import { InfoCard } from "@/components/molecules/Cards";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const benefits = [
  ["📲", "Sem abrir novo app", "Tudo via mensagem, no canal que você já usa todos os dias."],
  ["⚡", "Registro em segundos", "Uma mensagem curta já é suficiente. Sem campos, sem navegação."],
  ["📈", "Histórico organizado", "Seus ciclos ficam registrados e acessíveis quando você quiser."],
  ["🎯", "Previsões aproximadas", "A Luma estima a próxima menstruação com base no seu histórico."],
  ["🔔", "Lembretes opcionais", "Ative só o que faz sentido para a sua rotina."],
  ["💬", "Linguagem humana", "Nada de termos médicos ou menus confusos. Só conversa."],
  ["🔐", "Privacidade como prioridade", "Seus dados são seus. Pensado com consentimento desde o início."],
  ["🌙", "Simples e sem julgamentos", "Um espaço só seu, discreto e sem complicação."],
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
        />
        <div className="benefits-grid">
          {benefits.map(([icon, title, text], index) => (
            <InfoCard
              key={title}
              className="benefit"
              delay={`${0.05 * (index + 1)}s`}
              icon={<div className="benefit-icon">{icon}</div>}
              title={title}
              text={text}
            />
          ))}
        </div>
      </div>
    </section>
  );
}
