import { ButtonLink } from "@/components/atoms/Button";

export function DevelopmentSection() {
  return (
    <section className="desenvolvimento">
      <div className="section-inner fade-up">
        <span className="dev-badge">Em desenvolvimento</span>
        <h2 className="section-h2">
          Ainda estamos construindo.
          <br />
          <em>Você pode ajudar.</em>
        </h2>
        <p className="section-lead">
          A Luma ainda não está disponível. Estamos reunindo pessoas interessadas para testar a primeira versão e
          construir juntas uma experiência que realmente faça sentido na rotina.
        </p>
        <div className="dev-counter">
          <div className="dev-stat">
            <strong id="counter-num">0</strong>
            <span>já na lista</span>
          </div>
          <div className="dev-stat">
            <strong>1ª versão</strong>
            <span>em construção</span>
          </div>
        </div>
        <ButtonLink variant="primary" href="#precos">
          Quero meu plano
        </ButtonLink>
      </div>
    </section>
  );
}
