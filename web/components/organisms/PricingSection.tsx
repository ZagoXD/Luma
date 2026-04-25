import { PlanCard } from "@/components/molecules/PlanCard";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const baseFeatures = [
  { enabled: true, label: "Registro de menstruação por mensagem" },
  { enabled: true, label: "Registro de sintomas e fluxo" },
  { enabled: true, label: "Previsão da próxima menstruação" },
  { enabled: true, label: "Histórico do ciclo" },
];

export function PricingSection() {
  return (
    <section className="precos" id="precos">
      <div className="section-inner">
        <SectionHeading
          tag="Planos"
          title="Simples e transparente"
          lead="Escolha o plano que faz mais sentido para a sua rotina."
          centered
        />
        <div className="plans-grid">
          <PlanCard
            badge="Básico"
            badgeClass="basic"
            name="Básico"
            price={
              <>
                <span className="plan-currency">R$</span>
                <span className="plan-amount">
                  5<span style={{ fontSize: "1.4rem" }}>,90</span>
                </span>
                <span className="plan-period">/mês</span>
              </>
            }
            description="Tudo que você precisa para registrar seu ciclo de forma simples e sem esforço."
            features={[
              ...baseFeatures,
              { enabled: false, label: "Lembretes automáticos" },
              { enabled: false, label: "Lembrete de anticoncepcional" },
            ]}
            cta="Quero esse plano"
            ctaClass="basic-btn"
            note="Disponível no lançamento."
            delay="0.15s"
          />
          <PlanCard
            badge="Mais escolhido"
            badgeClass="full"
            name="Essencial"
            price={
              <>
                <span className="plan-currency">R$</span>
                <span className="plan-amount">
                  9<span style={{ fontSize: "1.4rem" }}>,90</span>
                </span>
                <span className="plan-period">/mês</span>
              </>
            }
            description="Tudo do plano Básico, mais lembretes inteligentes para nunca ser pega de surpresa."
            features={[
              ...baseFeatures,
              { enabled: true, label: "Aviso antes da próxima menstruação" },
              { enabled: true, label: "Lembrete de anticoncepcional" },
            ]}
            cta="Quero esse plano"
            ctaClass="full-btn"
            note="Disponível no lançamento."
            featured
            delay="0.25s"
          />
        </div>
      </div>
    </section>
  );
}
