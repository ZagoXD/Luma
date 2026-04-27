import { ButtonLink } from "@/components/atoms/Button";
import { PhoneFrame } from "@/components/molecules/PhoneFrame";

export function HeroSection() {
  return (
    <section className="hero">
      <div className="hero-text fade-up">
        <span className="hero-badge">Em breve</span>
        <h1>
          Seu ciclo,
          <br />
          <em>registrado</em>
          <br />
          em uma conversa.
        </h1>
        <p className="hero-sub">
          Chega de abrir aplicativo que você esquece de usar. Com a Luma, você usa direto pelo WhatsApp: é só mandar uma mensagem e ela organiza tudo.
        </p>
        <div className="hero-actions">
          <ButtonLink variant="primary" href="#lista">
            Entrar na lista de espera
          </ButtonLink>
          <ButtonLink variant="ghost" href="#como">
            Ver como funciona
          </ButtonLink>
        </div>
      </div>

      <div className="hero-visual fade-up" style={{ transitionDelay: "0.15s" }}>
        <PhoneFrame bodyId="phone-body" />
      </div>
    </section>
  );
}
