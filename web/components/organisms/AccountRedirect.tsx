"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { getAccountProfile } from "@/lib/luma-api";

export function AccountRedirect() {
  const router = useRouter();
  useEffect(() => {
    void getAccountProfile().then(profile => router.replace(`/perfil/${profile.user.id}`))
      .catch(() => router.replace("/login?redirect=/perfil"));
  }, [router]);
  return <main className="account-page"><p role="status">Carregando sua conta...</p></main>;
}
