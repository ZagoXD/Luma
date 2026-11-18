import type { Metadata, MetadataRoute } from "next";

export const productionOrigin = "https://ia-luma.com.br";
export const siteTitle = "Luma IA | Ciclo menstrual e gravidez pelo WhatsApp";
export const siteDescription = "Luma é sua assistente de IA no WhatsApp para registrar o ciclo menstrual, acompanhar a gravidez, consultar o calendário e configurar lembretes.";

export function siteOrigin(value = process.env.LUMA_SITE_URL || "http://localhost:3000"): string {
  try {
    const url = new URL(value);
    if (!["https:", "http:"].includes(url.protocol) || url.username || url.password) return "http://localhost:3000";
    return url.origin;
  } catch {
    return "http://localhost:3000";
  }
}

export function isIndexableSite(value?: string) {
  return siteOrigin(value) === productionOrigin;
}

export function isPrivatePath(path: string) {
  return ["/api", "/perfil", "/checkout", "/login", "/forgot-password", "/reset-password"]
    .some((prefix) => path === prefix || path.startsWith(prefix + "/"));
}

export function privateMetadata(title: string): Metadata {
  return { title, robots: { index: false, follow: false } };
}

export function buildMetadata(origin?: string): Metadata {
  const index = isIndexableSite(origin);
  return {
    metadataBase: new URL(siteOrigin(origin)),
    title: { default: siteTitle, template: "%s | Luma IA" },
    description: siteDescription,
    applicationName: "Luma IA",
    robots: { index, follow: index },
    openGraph: {
      type: "website", locale: "pt_BR", siteName: "Luma IA", title: siteTitle, description: siteDescription,
      images: [{ url: "/opengraph-image", width: 1200, height: 630, alt: "Luma IA - ciclo menstrual e gravidez pelo WhatsApp" }],
    },
    twitter: { card: "summary_large_image", title: siteTitle, description: siteDescription, images: ["/opengraph-image"] },
  };
}

export function buildRobots(origin?: string): MetadataRoute.Robots {
  if (!isIndexableSite(origin)) return { rules: { userAgent: "*", allow: "/" } };
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/api/", "/perfil", "/checkout", "/login", "/forgot-password", "/reset-password"] },
    sitemap: productionOrigin + "/sitemap.xml",
  };
}

export function buildSitemap(origin?: string): MetadataRoute.Sitemap {
  return isIndexableSite(origin) ? [{ url: productionOrigin + "/" }] : [];
}

export function buildStructuredData() {
  return {
    "@context": "https://schema.org",
    "@graph": [
      { "@type": "WebSite", "@id": productionOrigin + "/#website", url: productionOrigin + "/", name: "Luma IA",
        alternateName: "Luma", inLanguage: "pt-BR", publisher: { "@id": productionOrigin + "/#organization" } },
      { "@type": "Organization", "@id": productionOrigin + "/#organization", name: "Luma", alternateName: "Luma IA",
        url: productionOrigin + "/", logo: { "@type": "ImageObject", url: productionOrigin + "/icon", width: 96, height: 96 } },
    ],
  };
}
