import { NextRequest, NextResponse } from "next/server";
import { isIndexableSite, isPrivatePath, productionOrigin } from "./lib/seo";

const protectedRoutes = ["/perfil", "/checkout"];

export function proxy(request: NextRequest) {
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
