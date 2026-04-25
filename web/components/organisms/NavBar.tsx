"use client";

import { Button } from "@/components/atoms/Button";
import { Logo } from "@/components/atoms/Logo";

export function NavBar() {
  return (
    <nav id="nav">
      <Logo />
      <Button data-scroll-target="lista">Entrar na lista</Button>
    </nav>
  );
}
