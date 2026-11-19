import { NextRequest, NextResponse } from "next/server";
import { isIndexableSite, isPrivatePath, productionOrigin } from "./lib/seo";

const protectedRoutes = ["/perfil", "/checkout"];

export function proxy(request: NextRequest) {
  if (process.env.NEXT_PUBLIC_LUMA_SITE_MODE === "waitlist") {
    const path = request.nextUrl.pathname;
    if (["/api/auth", "/api/account", "/api/checkout", "/api/support"].some(prefix => path === prefix || path.startsWith(prefix + "/")))
      return NextResponse.json({ message: "A Luma está em lista de espera." }, { status: 404 });
    if (["/perfil", "/checkout", "/login", "/forgot-password", "/reset-password", "/suporte"].some(prefix => path === prefix || path.startsWith(prefix + "/")))
      return NextResponse.redirect(new URL("/#lista", request.url));
  }
  const annotate = (response: NextResponse) => {
    if (!isIndexableSite() || isPrivatePath(request.nextUrl.pathname)) response.headers.set("X-Robots-Tag", "noindex, nofollow");
    return response;
  };
  if (isIndexableSite() && request.headers.get("host")?.split(":")[0].toLowerCase() === "www.ia-luma.com.br") {
    const url = new URL(productionOrigin);
    url.pathname = request.nextUrl.pathname;
    url.search = request.nextUrl.search;
    return NextResponse.redirect(url, 308);
  }
  const isProtected = protectedRoutes.some((route) => request.nextUrl.pathname.startsWith(route));
  if (!isProtected || request.cookies.has("luma_session")) {
    return annotate(NextResponse.next());
  }

  const loginUrl = new URL("/login", request.url);
  loginUrl.searchParams.set("redirect", `${request.nextUrl.pathname}${request.nextUrl.search}`);
  return annotate(NextResponse.redirect(loginUrl));
}

export const config = {
  matcher: ["/((?!_next/static|_next/image).*)"],
};
