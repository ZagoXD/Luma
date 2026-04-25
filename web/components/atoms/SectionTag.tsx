import type { ReactNode } from "react";

export function SectionTag({ children }: { children: ReactNode }) {
  return <p className="section-tag">{children}</p>;
}
