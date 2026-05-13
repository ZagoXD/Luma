import { NextResponse } from "next/server";
import { readJsonBody, setSessionCookie } from "@/lib/server-api";

const apiBaseUrl =
  process.env.LUMA_API_BASE_URL ||
  process.env.NEXT_PUBLIC_API_BASE_URL ||
  "http://localhost:5050";

export async function POST(request: Request) {
  const body = await readJsonBody(request);
  const apiResponse = await fetch(`${apiBaseUrl}/account/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
  const data = await apiResponse.json().catch(() => ({}));

  if (!apiResponse.ok || !data.token) {
    if (apiResponse.status === 401) {
      return NextResponse.json({ message: "E-mail ou senha inválidos." }, { status: 400 });
    }

    return NextResponse.json(data, { status: apiResponse.status });
  }

  const response = NextResponse.json({ user: data.user });
  setSessionCookie(response, data.token);
  return response;
}
