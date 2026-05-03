import { ButtonLink } from "@/components/atoms/Button";

export function DevelopmentSection() {
  return (
    <section className="desenvolvimento">
      <div className="section-inner fade-up">
        <span className="dev-badge">Lançamento</span>
        <h2 className="section-h2">
          Comece hoje.
          <br />
          <em>A Luma acompanha você.</em>
        </h2>
        <p className="section-lead">
          Ative seu plano, cadastre seu número e converse com a Luma pelo WhatsApp
          para registrar ciclo, sintomas, gravidez, lembretes e histórico.
        </p>
        <div className="dev-counter">
          <div className="dev-stat">
            <strong id="counter-num">WhatsApp</strong>
            <span>atendimento direto</span>
          </div>
          <div className="dev-stat">
            <strong>V1</strong>
            <span>pronta para uso</span>
          </div>
        </div>
        <ButtonLink variant="primary" href="#precos">
          Quero meu plano
        </ButtonLink>
      </div>
    </section>
  );
}
