"use client";

import Link from "next/link";
import { Logo } from "@/components/atoms/Logo";
import { isWaitlist } from "@/lib/site-mode";

export function NavBar() {
  return (
    <nav id="nav">
      <Logo />
      <div className="nav-actions">
        <Link className="nav-link" href="/#precos">Planos</Link>
        <Link className="nav-cta" href={isWaitlist ? "/#lista" : "/perfil"}>{isWaitlist ? "Entrar na lista" : "Meu perfil"}</Link>
      </div>
    </nav>
  );
}
