import { NextResponse } from "next/server";
import { validateWaitlist, waitlistSuccessMessage } from "@/lib/waitlist";
export async function POST(request: Request) {
  if (process.env.NEXT_PUBLIC_LUMA_SITE_MODE !== "waitlist") return new Response(null, { status: 404 });
  if (request.headers.get("origin") !== new URL(request.url).origin) return new Response(null, { status: 403 });
  if (Number(request.headers.get("content-length")) > 4096) return new Response(null, { status: 413 });
  let body;
  try { const text = await request.text(); if (text.length > 4096) return new Response(null, { status: 413 }); body = JSON.parse(text); }
  catch { return NextResponse.json({ message: "Confira os campos do formulário." }, { status: 400 }); }
  if (!body || typeof body !== "object") return new Response(null, { status: 400 });
  if (body.website) return NextResponse.json({ message: "Interesse registrado. Obrigada!" });
  const input = validateWaitlist(body);
  if (!input) return NextResponse.json({ message: "Confira os contatos dos canais escolhidos e marque separadamente os dois aceites." }, { status: 400 });
  const endpoint = process.env.WAITLIST_API_URL;
  const secret = process.env.WAITLIST_API_SECRET;
  if (!endpoint || !secret) return NextResponse.json({ message: "A lista está temporariamente indisponível. Tente novamente mais tarde." }, { status: 503 });
  try {
    const response = await fetch(endpoint, { method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${secret}` },
      body: JSON.stringify({ ...input, ip: request.headers.get("x-vercel-forwarded-for")?.split(",")[0]?.trim() || request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() || "unknown" }), signal: AbortSignal.timeout(10000), cache: "no-store" });
    if (!response.ok) return NextResponse.json({ message: response.status === 429 ? "Aguarde um pouco antes de tentar novamente." : "Não conseguimos registrar agora. Tente novamente mais tarde." }, { status: response.status === 429 ? 429 : 503 });
    return NextResponse.json({ message: waitlistSuccessMessage });
  } catch { return NextResponse.json({ message: "Não conseguimos registrar agora. Tente novamente mais tarde." }, { status: 503 }); }
}
