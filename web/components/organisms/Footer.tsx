import Link from "next/link";
export function Footer() {
  return (
    <footer>
      <div className="footer-logo">Luma</div>
      <p>© 2026 Luma · Assistente de ciclo e gravidez · Não substitui orientação médica</p>
      <p><Link href="/termos">Termos de Uso</Link> · <Link href="/privacidade">Privacidade</Link> · <a href="mailto:skyziinxd@gmail.com">Contato</a></p>
    </footer>
  );
}
