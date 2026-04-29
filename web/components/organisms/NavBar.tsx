"use client";

import Link from "next/link";
import { Logo } from "@/components/atoms/Logo";

export function NavBar() {
  return (
    <nav id="nav">
      <Logo />
      <div className="nav-actions">
        <Link className="nav-link" href="/#precos">Planos</Link>
        <Link className="nav-cta" href="/perfil">Meu perfil</Link>
      </div>
    </nav>
  );
}
