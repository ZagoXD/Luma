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
          lead="Escolha o plano anual para economizar ou siga com a cobrança mensal tradicional."
          centered
        />
        <div className="plans-grid">
          <PlanCard
            badge="Básico"
            badgeClass="basic"
            name="Básico"
            planValue="basico"
            price={
              <div className="plan-price-stack">
                <span className="plan-price-line">R$ 5,90/mês</span>
                <span className="plan-period">no plano anual cobrado à vista: R$ 70,80/ano</span>
                <span className="plan-monthly-line">Plano mensal: R$ 11,00/mês</span>
              </div>
            }
            description="Tudo que você precisa para registrar seu ciclo de forma simples e sem esforço."
            features={[
              ...baseFeatures,
              { enabled: false, label: "Mensagens por áudio" },
              { enabled: false, label: "Imagens educativas do bebê" },
              { enabled: false, label: "Notificações e lembretes automáticos" },
            ]}
            cta="Assinar anual"
            secondaryCta="Assinar mensal"
            ctaClass="basic-btn"
            note="No anual, o acesso fica ativo por 12 meses. Cancelar impede a próxima renovação."
            secondaryNote="No mensal, a cobrança é feita a cada mês."
            delay="0.15s"
          />
          <PlanCard
            badge="Mais escolhido"
            badgeClass="full"
            name="Essencial"
            planValue="essencial"
            price={
              <div className="plan-price-stack">
                <span className="plan-price-line">R$ 9,90/mês</span>
                <span className="plan-period">no plano anual cobrado à vista: R$ 118,80/ano</span>
                <span className="plan-monthly-line">Plano mensal: R$ 20,00/mês</span>
              </div>
            }
            description="Tudo do plano Básico, mais recursos inteligentes para uma rotina com menos esforço."
            features={[
              ...baseFeatures,
              { enabled: true, label: "Mensagens por áudio" },
              { enabled: true, label: "Imagens educativas do bebê" },
              { enabled: true, label: "Notificações e lembretes automáticos" },
              { enabled: true, label: "Acompanhamento de gravidez com mais contexto" },
            ]}
            cta="Assinar anual"
            secondaryCta="Assinar mensal"
            ctaClass="full-btn"
            note="No anual, o acesso fica ativo por 12 meses. Cancelar impede a próxima renovação."
            secondaryNote="No mensal, a cobrança é feita a cada mês."
            featured
            delay="0.25s"
          />
        </div>
      </div>
    </section>
  );
}
