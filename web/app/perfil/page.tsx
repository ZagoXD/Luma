import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { sessionCookieName } from "@/lib/server-api";

const apiBaseUrl =
  process.env.LUMA_API_BASE_URL ||
  process.env.NEXT_PUBLIC_API_BASE_URL ||
  "http://localhost:5050";

export default async function ProfileRedirectPage() {
  const cookieStore = await cookies();
  const token = cookieStore.get(sessionCookieName)?.value;
  if (!token) {
    redirect("/login?redirect=/perfil");
  }

  const response = await fetch(`${apiBaseUrl}/account/me`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (!response.ok) {
    redirect("/login?redirect=/perfil");
  }

  const profile = await response.json() as { user: { id: string } };
  redirect(`/perfil/${profile.user.id}`);
}
