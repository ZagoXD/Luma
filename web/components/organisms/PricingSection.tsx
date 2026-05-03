import { PlanCard } from "@/components/molecules/PlanCard";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const baseFeatures = [
  { enabled: true, label: "Mensagens de texto pelo WhatsApp" },
  { enabled: true, label: "Registro de menstruação, sintomas e fluxo" },
  { enabled: true, label: "Previsão da próxima menstruação" },
  { enabled: true, label: "Histórico e calendário visual do ciclo" },
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
            planValue="basico"
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
              { enabled: false, label: "Mensagens por áudio" },
              { enabled: false, label: "Imagens educativas do bebê" },
              { enabled: false, label: "Notificações e lembretes automáticos" },
            ]}
            cta="Quero esse plano"
            ctaClass="basic-btn"
            note="Libera o bot no WhatsApp."
            delay="0.15s"
          />
          <PlanCard
            badge="Mais escolhido"
            badgeClass="full"
            name="Essencial"
            planValue="essencial"
            price={
              <>
                <span className="plan-currency">R$</span>
                <span className="plan-amount">
                  9<span style={{ fontSize: "1.4rem" }}>,90</span>
                </span>
                <span className="plan-period">/mês</span>
              </>
            }
            description="Tudo do plano Básico, mais recursos inteligentes para uma rotina com menos esforço."
            features={[
              ...baseFeatures,
              { enabled: true, label: "Mensagens por áudio" },
              { enabled: true, label: "Imagens educativas do bebê" },
              { enabled: true, label: "Notificações e lembretes automáticos" },
              { enabled: true, label: "Acompanhamento de gravidez com mais contexto" },
            ]}
            cta="Quero esse plano"
            ctaClass="full-btn"
            note="Libera o bot no WhatsApp."
            featured
            delay="0.25s"
          />
        </div>
      </div>
    </section>
  );
}
